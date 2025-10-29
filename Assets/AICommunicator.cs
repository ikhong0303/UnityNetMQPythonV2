using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[Serializable]
public class ServerState { public string state; public string gesture; public string audio_path; }

public class AICommunicator : MonoBehaviour
{
    [Header("UI (선택 사항)")]
    public Image statusIcon;
    public Color idleColor = Color.gray;
    public Color listenColor = Color.yellow;
    public Color talkColor = Color.green;

    [Header("연결 대상")]
    public Animator avatarAnimator;
    public AudioSource audioSource;

    [Header("연결 설정")]
    public string serverAddress = "tcp://localhost:5555";

    private Thread _networkThread;
    private bool _isRunning;
    private readonly ConcurrentQueue<string> _stateQueue = new ConcurrentQueue<string>();
    private string _currentAvatarState = ""; // 초기 상태 비우기

    void Start()
    {
        _isRunning = true;
        _networkThread = new Thread(NetworkLoop);
        _networkThread.Start();

        // 애니메이터가 시작 시 자동으로 Idle 상태로 가도록 코드는 제거
        // SetState("Idle"); // 이 줄 제거 또는 주석 처리
        if (statusIcon != null) statusIcon.color = idleColor;
    }

    void OnDestroy()
    {
        _isRunning = false;
        _networkThread?.Join(1000);
        NetMQConfig.Cleanup(false);
    }

    void Update()
    {
        if (_stateQueue.TryDequeue(out var jsonState))
        {
            ServerState newState = JsonUtility.FromJson<ServerState>(jsonState);

            // 동일 상태 중복 방지 (Idle과 Cooldown은 중복 허용 안함)
            if (newState.state == _currentAvatarState) return;

            // 상태 변경 처리
            SetState(newState.state, newState);
        }
    }

    void SetState(string state, ServerState data = null)
    {
        _currentAvatarState = state;
        Debug.Log($"상태 변경 수신: {_currentAvatarState}");

        // UI 업데이트
        if (statusIcon != null)
        {
            if (state == "Idle" || state == "Cooldown") statusIcon.color = idleColor;
            else if (state == "Listen") statusIcon.color = listenColor;
            else if (state == "Talk") statusIcon.color = talkColor;
        }

        // 상태에 따른 애니메이션 트리거 실행
        if (state == "Talk" && data != null)
        {
            StartCoroutine(LoadAndPlayAudio(data.audio_path, data.gesture));
        }
        else if (state == "Listen")
        {
            avatarAnimator.SetTrigger("Listen"); // "Listen" 트리거만 보냄
        }
        else if (state == "Idle")
        {
            avatarAnimator.SetTrigger("Idle"); // "Idle" 트리거만 보냄
        }
        // Cooldown 상태일 때는 아무 애니메이션도 실행하지 않음
    }

    private IEnumerator LoadAndPlayAudio(string path, string gesture)
    {
        string audioPath = "file://" + path.Replace("\\", "/");

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(audioPath, AudioType.WAV))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

                if (avatarAnimator != null && !string.IsNullOrEmpty(gesture))
                {
                    avatarAnimator.SetTrigger(gesture); // "Talk" 트리거만 보냄
                }
                audioSource.PlayOneShot(clip);
                yield return new WaitForSeconds(clip.length);
            }
            else { Debug.LogError($"오디오 파일 로드 실패: {www.error}"); }
        }

        // 오디오 재생이 끝나면 파이썬이 Cooldown -> Idle 신호를 보내줄 때까지 기다림
        // SetState("Idle"); // 이 줄 제거 또는 주석 처리
    }

    private void NetworkLoop()
    {
        AsyncIO.ForceDotNet.Force();
        using (var subSocket = new SubscriberSocket())
        {
            subSocket.Connect(serverAddress);
            subSocket.SubscribeToAnyTopic();
            while (_isRunning)
            {
                if (subSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out var message)) // 타임아웃 추가
                {
                    _stateQueue.Enqueue(message);
                }
                // Thread.Sleep(10); 제거 - TryReceiveFrameString이 블로킹 역할
            }
        }
    }
}