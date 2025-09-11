using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class AudioPacket { public int channels; public string audio_b64; }
[Serializable]
public class AIResponse { public string gesture; public string audio_b64; }

public class AICommunicator : MonoBehaviour
{
    [Header("데시벨 감지 설정")]
    public Slider decibelSlider;
    public Image recordingIcon;
    [Range(0.001f, 0.5f)]
    public float volumeThreshold = 0.02f;
    public int recordDuration = 5;

    [Header("UI 설정")]
    public float sliderSmoothing = 8f;

    [Header("연결 대상")]
    public Animator avatarAnimator;
    public AudioSource audioSource;

    [Header("연결 설정")]
    public string serverAddress = "tcp://localhost:5555";

    private const int SampleRate = 44100;
    private const int MonitorClipLengthSec = 10; // 상시 녹음 클립 길이 (넉넉하게)

    private Thread _networkThread;
    private bool _isRunning;
    private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();
    private string _micDevice;
    private AudioClip _monitoringClip;
    private float[] _monitorSampleData = new float[1024];
    private bool _isRecording = false;
    private float _sliderCurrentValue = 0f;

    void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("마이크를 찾을 수 없습니다!");
            return;
        }
        _micDevice = Microphone.devices[0];

        if (recordingIcon != null) recordingIcon.enabled = false;

        // --- 핵심 변경: 게임 시작 시 한 번만, 길고 반복되는 녹음 클립을 시작합니다. ---
        _monitoringClip = Microphone.Start(_micDevice, true, MonitorClipLengthSec, SampleRate);

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
        if (_monitoringClip == null) return;

        // 데시벨 계산 및 슬라이더 업데이트 (이전과 동일)
        int micPosition = Microphone.GetPosition(_micDevice);
        int startReadPosition = micPosition - _monitorSampleData.Length;
        if (startReadPosition < 0) return;

        _monitoringClip.GetData(_monitorSampleData, startReadPosition);

        float currentLinearVolume = 0;
        foreach (var sample in _monitorSampleData)
        {
            currentLinearVolume += Mathf.Abs(sample);
        }
        currentLinearVolume /= _monitorSampleData.Length;

        float targetSliderValue = 0f;
        if (currentLinearVolume > 0.0001f)
        {
            float db = 20 * Mathf.Log10(currentLinearVolume);
            targetSliderValue = Mathf.InverseLerp(-60f, 0f, db);
        }
        _sliderCurrentValue = Mathf.Lerp(_sliderCurrentValue, targetSliderValue, Time.deltaTime * sliderSmoothing);
        if (decibelSlider != null) decibelSlider.value = _sliderCurrentValue;

        // 녹음 시작 조건
        if (!_isRecording && currentLinearVolume > volumeThreshold)
        {
            StartCoroutine(ProcessRecording());
        }
    }

    // --- 핵심 변경: 녹음 시퀀스가 마이크를 껐다 켜지 않고 데이터만 복사합니다. ---
    private IEnumerator ProcessRecording()
    {
        _isRecording = true;

        avatarAnimator.SetTrigger("Listen");
        if (recordingIcon != null) recordingIcon.enabled = true;
        Debug.Log("소리 감지! 5초 후 데이터를 추출합니다...");

        // 마이크를 끄는 대신, 5초를 기다립니다. 그동안 마이크는 계속 녹음 중입니다.
        yield return new WaitForSeconds(recordDuration);

        // 5초 대기 후, 현재 위치에서 5초 분량의 오디오 데이터를 잘라냅니다.
        int endPosition = Microphone.GetPosition(_micDevice);
        int sampleCount = recordDuration * SampleRate;

        float[] recordedSamples = new float[sampleCount * _monitoringClip.channels];

        // 데이터가 클립의 시작 부분과 끝 부분에 걸쳐있는 경우(Wrap-around) 처리
        int startPosition = endPosition - sampleCount;
        if (startPosition < 0)
        {
            int remainingSamples = -startPosition;
            _monitoringClip.GetData(recordedSamples, _monitoringClip.samples - remainingSamples);
            _monitoringClip.GetData(new Span<float>(recordedSamples, remainingSamples, endPosition), 0);
        }
        else
        {
            _monitoringClip.GetData(recordedSamples, startPosition);
        }

        // 이후 데이터 처리 및 전송은 동일
        byte[] audioBytes = new byte[recordedSamples.Length * 4];
        Buffer.BlockCopy(recordedSamples, 0, audioBytes, 0, audioBytes.Length);

        AudioPacket packet = new AudioPacket
        {
            channels = _monitoringClip.channels,
            audio_b64 = Convert.ToBase64String(audioBytes)
        };
        string jsonRequest = JsonUtility.ToJson(packet);
        _sendQueue.Enqueue(jsonRequest);

        Debug.Log($"데이터 추출 완료. (채널: {_monitoringClip.channels})");

        if (recordingIcon != null) recordingIcon.enabled = false;

        // 다시 녹음이 가능하도록 플래그를 풀어줍니다.
        // 약간의 딜레이를 주어 연속적인 녹음을 방지합니다.
        yield return new WaitForSeconds(1f);
        _isRecording = false;
    }

    // --- 나머지 코드는 이전과 동일합니다 ---
    #region Unchanged Code
    private void NetworkLoop()
    {
        AsyncIO.ForceDotNet.Force();
        using (var client = new RequestSocket())
        {
            client.Connect(serverAddress);
            while (_isRunning)
            {
                if (_sendQueue.TryDequeue(out var jsonRequest))
                {
                    client.TrySendFrame(jsonRequest);
                    if (client.TryReceiveFrameString(TimeSpan.FromSeconds(20), out var jsonResponse))
                    {
                        UnityMainThreadDispatcher.Instance().Enqueue(() => ProcessResponse(jsonResponse));
                    }
                    else Debug.LogError("서버 응답 시간 초과!");
                }
                Thread.Sleep(50);
            }
        }
    }

    private void ProcessResponse(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        AIResponse response = JsonUtility.FromJson<AIResponse>(json);

        if (audioSource != null && !string.IsNullOrEmpty(response.audio_b64))
        {
            byte[] responseAudioBytes = Convert.FromBase64String(response.audio_b64);
            AudioClip clip = WavUtility.ToAudioClip(responseAudioBytes);
            if (clip != null)
            {
                StartCoroutine(PlayAudioAndAnimate(clip, response.gesture));
            }
        }
    }

    private IEnumerator PlayAudioAndAnimate(AudioClip clip, string gesture)
    {
        if (avatarAnimator != null && !string.IsNullOrEmpty(gesture))
        {
            avatarAnimator.SetTrigger("Talk");
        }

        audioSource.PlayOneShot(clip);

        yield return new WaitForSeconds(clip.length);

        avatarAnimator.SetTrigger("Idle");
    }
    #endregion
}