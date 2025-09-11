using System;
using System.IO;
using UnityEngine;

// 받은 WAV 바이트 배열을 AudioClip으로 변환해주는 유틸리티
public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] fileBytes)
    {
        try
        {
            using (var memoryStream = new MemoryStream(fileBytes))
            {
                using (var reader = new BinaryReader(memoryStream))
                {
                    // WAV 헤더 정보 읽기
                    int chunkID = reader.ReadInt32();
                    int fileSize = reader.ReadInt32();
                    int riffType = reader.ReadInt32();
                    int fmtID = reader.ReadInt32();
                    int fmtSize = reader.ReadInt32();
                    int audioFormat = reader.ReadInt16();
                    int channels = reader.ReadInt16();
                    int sampleRate = reader.ReadInt32();
                    int byteRate = reader.ReadInt32();
                    int blockAlign = reader.ReadInt16();
                    int bitsPerSample = reader.ReadInt16();

                    // "data" 청크 찾기
                    while (reader.ReadInt32() != 0x61746164)
                    {
                        int chunkSize = reader.ReadInt32();
                        reader.ReadBytes(chunkSize);
                    }

                    int dataSize = reader.ReadInt32();
                    byte[] data = reader.ReadBytes(dataSize);

                    float[] floatArray = new float[data.Length / 2];
                    for (int i = 0; i < floatArray.Length; i++)
                    {
                        floatArray[i] = BitConverter.ToInt16(data, i * 2) / 32768.0f;
                    }

                    AudioClip audioClip = AudioClip.Create("ServerResponse", floatArray.Length, channels, sampleRate, false);
                    audioClip.SetData(floatArray, 0);

                    return audioClip;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("WAV 데이터를 AudioClip으로 변환하는 중 오류 발생: " + e.Message);
            return null;
        }
    }
}