using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking; // UnityWebRequest를 사용하기 위해 추가
using UnityEngine.UI;

// JSON 응답을 받기 위한 데이터 구조
[Serializable]
public class PythonResponse { public string gesture; public string audio_path; }

public class AICommunicator : MonoBehaviour
{
    [Header("데시벨 감지 설정")]
    public Slider decibelSlider;
    public Image recordingIcon;
    [Range(0.001f, 0.5f)]
    public float volumeThreshold = 0.02f;

    [Header("UI 설정")]
    public float sliderSmoothing = 8f;

    [Header("연결 대상")]
    public Animator avatarAnimator;
    public AudioSource audioSource;

    [Header("연결 설정")]
    public string serverAddress = "tcp://localhost:5555";

    private Thread _networkThread;
    private bool _isRunning;
    // 이제 요청은 간단한 문자열이므로 ConcurrentQueue<string>으로 변경
    private readonly ConcurrentQueue<string> _requestQueue = new ConcurrentQueue<string>();
    private string _micDevice;
    private AudioClip _monitoringClip;
    private float[] _monitorSampleData = new float[1024];
    private bool _isWaitingForServer = false; // 서버 응답 대기 중복 방지 플래그
    private float _sliderCurrentValue = 0f;

    void Start()
    {
        if (Microphone.devices.Length == 0) { Debug.LogError("마이크를 찾을 수 없습니다!"); return; }
        _micDevice = Microphone.devices[0];
        if (recordingIcon != null) recordingIcon.enabled = false;

        _monitoringClip = Microphone.Start(_micDevice, true, 10, 44100);

        _isRunning = true;
        _networkThread = new Thread(NetworkLoop);
        _networkThread.Start();
    }

    void OnDestroy()
    {
        _isRunning = false;
        if (!string.IsNullOrEmpty(_micDevice)) Microphone.End(_micDevice);
        _networkThread?.Join(1000);
        NetMQConfig.Cleanup(false);
    }

    void Update()
    {
        // 데시벨 감지 및 슬라이더 업데이트 로직 (이전과 동일)
        if (_monitoringClip == null) return;
        int micPosition = Microphone.GetPosition(_micDevice);
        int startReadPosition = micPosition - _monitorSampleData.Length;
        if (startReadPosition < 0) return;
        _monitoringClip.GetData(_monitorSampleData, startReadPosition);
        float currentLinearVolume = 0;
        foreach (var sample in _monitorSampleData) currentLinearVolume += Mathf.Abs(sample);
        currentLinearVolume /= _monitorSampleData.Length;
        float targetSliderValue = 0f;
        if (currentLinearVolume > 0.0001f)
        {
            float db = 20 * Mathf.Log10(currentLinearVolume);
            targetSliderValue = Mathf.InverseLerp(-60f, 0f, db);
        }
        _sliderCurrentValue = Mathf.Lerp(_sliderCurrentValue, targetSliderValue, Time.deltaTime * sliderSmoothing);
        if (decibelSlider != null) decibelSlider.value = _sliderCurrentValue;

        // --- 핵심 변경: 녹음 대신 '신호'를 보냄 ---
        if (!_isWaitingForServer && currentLinearVolume > volumeThreshold)
        {
            _isWaitingForServer = true; // 중복 신호 방지
            avatarAnimator.SetTrigger("Listen");
            if (recordingIcon != null) recordingIcon.enabled = true;
            Debug.Log("소리 감지! 파이썬에 녹음 시작 신호를 보냅니다...");
            _requestQueue.Enqueue("START_RECORDING");
        }
    }

    private void NetworkLoop()
    {
        AsyncIO.ForceDotNet.Force();
        using (var client = new RequestSocket())
        {
            client.Connect(serverAddress);
            while (_isRunning)
            {
                if (_requestQueue.TryDequeue(out var message))
                {
                    client.TrySendFrame(message); // "START_RECORDING" 신호 전송
                    if (client.TryReceiveFrameString(TimeSpan.FromSeconds(20), out var jsonResponse))
                    {
                        // 메인 스레드에서 응답 처리
                        UnityMainThreadDispatcher.Instance().Enqueue(() => ProcessResponse(jsonResponse));
                    }
                    else
                    {
                        Debug.LogError("서버 응답 시간 초과!");
                        _isWaitingForServer = false; // 응답 실패 시 다시 감지 가능하도록
                    }
                }
                Thread.Sleep(50);
            }
        }
    }

    private void ProcessResponse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            _isWaitingForServer = false;
            return;
        }

        Debug.Log("파이썬으로부터 응답 수신: " + json);
        PythonResponse response = JsonUtility.FromJson<PythonResponse>(json);

        if (recordingIcon != null) recordingIcon.enabled = false;

        if (!string.IsNullOrEmpty(response.audio_path))
        {
            // 파일 경로로부터 오디오 클립을 로드하고 재생하는 코루틴 시작
            StartCoroutine(LoadAndPlayAudio(response.audio_path, response.gesture));
        }
        else
        {
            _isWaitingForServer = false; // 오디오 경로가 없으면 다시 감지 시작
        }
    }

    // --- 핵심 변경: 파일 경로에서 오디오를 로드하는 코루틴 ---
    private IEnumerator LoadAndPlayAudio(string path, string gesture)
    {
        // "file://" 접두사를 붙여 로컬 파일 경로임을 명시
        string audioPath = "file://" + path;

        // UnityWebRequest를 사용해 오디오 파일을 비동기적으로 로드
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(audioPath, AudioType.WAV))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

                // 애니메이션 및 오디오 재생
                if (avatarAnimator != null && !string.IsNullOrEmpty(gesture))
                {
                    avatarAnimator.SetTrigger(gesture);
                }
                audioSource.PlayOneShot(clip);
                yield return new WaitForSeconds(clip.length);
            }
            else
            {
                Debug.LogError("오디오 파일 로드 실패: " + www.error);
            }
        }

        // 모든 과정이 끝나면 Idle 상태로 돌아가고, 다시 소리 감지 시작
        avatarAnimator.SetTrigger("Idle");
        _isWaitingForServer = false;
    }
}