using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// JSON 상태 메시지를 받기 위한 데이터 구조
[Serializable]
public class ServerState
{
    public string state;
    public string gesture;
    public string audio_path;
}

public class AICommunicator : MonoBehaviour
{
    [Header("UI (선택 사항)")]
    public Image statusIcon; // 현재 상태를 표시할 아이콘
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
    private string _currentAvatarState = "Idle";

    void Start()
    {
        _isRunning = true;
        _networkThread = new Thread(NetworkLoop);
        _networkThread.Start();

        // 시작 시 Idle 상태로 초기화
        avatarAnimator.SetTrigger("Idle");
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

            if (newState.state != _currentAvatarState)
            {
                _currentAvatarState = newState.state;
                Debug.Log($"상태 변경 수신: {_currentAvatarState}");

                if (statusIcon != null)
                {
                    if (_currentAvatarState == "Idle") statusIcon.color = idleColor;
                    else if (_currentAvatarState == "Listen") statusIcon.color = listenColor;
                    else if (_currentAvatarState == "Talk") statusIcon.color = talkColor;
                    else if (_currentAvatarState == "Cooldown") statusIcon.color = idleColor;
                }

                if (_currentAvatarState == "Talk")
                {
                    StartCoroutine(LoadAndPlayAudio(newState.audio_path, newState.gesture));
                }
                else if (_currentAvatarState == "Listen" || _currentAvatarState == "Idle")
                {
                    avatarAnimator.SetTrigger(_currentAvatarState);
                }
            }
        }
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
                if (subSocket.TryReceiveFrameString(out var message))
                {
                    _stateQueue.Enqueue(message);
                }
                Thread.Sleep(10);
            }
        }
    }

    private IEnumerator LoadAndPlayAudio(string path, string gesture)
    {
        string audioPath = "file://" + path.Replace("\\", "/"); // 경로 구분자를 슬래시로 통일

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(audioPath, AudioType.WAV))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

                if (avatarAnimator != null && !string.IsNullOrEmpty(gesture))
                {
                    avatarAnimator.SetTrigger(gesture);
                }
                audioSource.PlayOneShot(clip);
                yield return new WaitForSeconds(clip.length);
            }
            else
            {
                Debug.LogError("오디오 파일 로드 실패: " + www.error + " (경로: " + audioPath + ")");
            }
        }
    }
}