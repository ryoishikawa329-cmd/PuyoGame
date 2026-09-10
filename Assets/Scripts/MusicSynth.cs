using System;   // Array.IndexOf
using System.Collections.Generic;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// BGM をその場で合成する。矩形波・三角波を重ねたチップチューン風。
    /// </summary>
    public static class MusicSynth
    {
        public const int SampleRate = 44100;

        /// <summary>音符ひとつ。Note が0以下なら休符。</summary>
        public readonly struct Step
        {
            public readonly int Note;      // MIDIノート番号
            public readonly float Beats;

            public Step(int note, float beats)
            {
                Note = note;
                Beats = beats;
            }
        }

        static Step N(int note, float beats = 1f) => new Step(note, beats);
        static Step Rest(float beats = 1f) => new Step(0, beats);

        // よく使う音（C4 = 60）
        const int C4 = 60, D4 = 62, E4 = 64, F4 = 65, G4 = 67, A4 = 69, B4 = 71;
        const int C5 = 72, D5 = 74, E5 = 76, F5 = 77, G5 = 79, A5 = 81, B5 = 83, C6 = 84;
        const int C3 = 48, E3 = 52, F3 = 53, G3 = 55, A3 = 57;
        const int C2 = 36, E2 = 40, F2 = 41, G2 = 43, A2 = 45;

        static float Frequency(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        /// <summary>音の立ち上がりと減衰。急に鳴って急に切れるのを防ぐ。</summary>
        public readonly struct Adsr
        {
            public readonly float Attack, Decay, Sustain, Release;

            public Adsr(float attack, float decay, float sustain, float release)
            {
                Attack = attack;
                Decay = decay;
                Sustain = sustain;
                Release = release;
            }

            public float Evaluate(float t, float holdSeconds)
            {
                if (t < 0f) return 0f;
                if (t < Attack) return Attack > 0f ? t / Attack : 1f;

                float afterAttack = t - Attack;
                if (afterAttack < Decay)
                    return Mathf.Lerp(1f, Sustain, Decay > 0f ? afterAttack / Decay : 1f);

                if (t < holdSeconds) return Sustain;

                float afterHold = t - holdSeconds;
                if (afterHold >= Release) return 0f;
                return Sustain * (1f - afterHold / Mathf.Max(Release, 1e-4f));
            }

            public float TotalSeconds(float holdSeconds) => holdSeconds + Release;
        }

        /// <summary>
        /// 正弦波に倍音を薄く足した音色。矩形波よりも角が取れて聴きやすい。
        /// </summary>
        static float Additive(float phase, float h2, float h3)
        {
            float v = Mathf.Sin(phase) + h2 * Mathf.Sin(phase * 2f) + h3 * Mathf.Sin(phase * 3f);
            return v / (1f + h2 + h3);
        }

        // ハ長調の音階。和音を重ねるときに調から外れないようにするため。
        static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };

        /// <summary>音階に沿って上に積んだ音を返す（3度なら steps=2、5度なら steps=4）。</summary>
        static int ScaleUp(int midi, int steps)
        {
            int pitchClass = ((midi % 12) + 12) % 12;
            int index = Array.IndexOf(MajorScale, pitchClass);
            if (index < 0) return midi + (steps == 2 ? 4 : 7);   // 音階外はそのまま積む

            int target = index + steps;
            int octave = target / MajorScale.Length;
            return midi + (MajorScale[target % MajorScale.Length] - pitchClass) + octave * 12;
        }

        static int LengthInSamples(IEnumerable<Step> steps, float bpm)
        {
            float beats = 0f;
            foreach (var s in steps) beats += s.Beats;
            return Mathf.CeilToInt(beats * (60f / bpm) * SampleRate);
        }

        /// <summary>
        /// 音符の並びをバッファに書き足す。
        /// chordGain を渡すと、音階に沿った3度・5度を薄く重ねて和音にする。
        /// </summary>
        static void AddTrack(float[] buffer, float bpm, IEnumerable<Step> steps,
                             Adsr adsr, float gain, float gate = 0.85f,
                             float h2 = 0.35f, float h3 = 0.12f, float chordGain = 0f)
        {
            float secondsPerBeat = 60f / bpm;
            int pos = 0;

            // 音符ごとに確保し直さないよう、外で用意しておく
            var notes = new int[3];
            var gains = new float[3];

            foreach (var step in steps)
            {
                int length = Mathf.RoundToInt(step.Beats * secondsPerBeat * SampleRate);
                if (step.Note > 0)
                {
                    float hold = step.Beats * secondsPerBeat * gate;
                    int total = Mathf.CeilToInt(adsr.TotalSeconds(hold) * SampleRate);

                    // ルート音と、和音として重ねる音
                    int count = 1;
                    notes[0] = step.Note; gains[0] = gain;
                    if (chordGain > 0f)
                    {
                        notes[1] = ScaleUp(step.Note, 2); gains[1] = gain * chordGain;
                        notes[2] = ScaleUp(step.Note, 4); gains[2] = gain * chordGain * 0.8f;
                        count = 3;
                    }

                    for (int v = 0; v < count; v++)
                    {
                        float increment = 2f * Mathf.PI * Frequency(notes[v]) / SampleRate;
                        float phase = 0f;
                        for (int i = 0; i < total && pos + i < buffer.Length; i++)
                        {
                            float t = i / (float)SampleRate;
                            buffer[pos + i] += Additive(phase, h2, h3) * gains[v] * adsr.Evaluate(t, hold);
                            phase += increment;
                        }
                    }
                }
                pos += length;
                if (pos >= buffer.Length) break;
            }
        }

        static AudioClip Finish(string name, float[] buffer)
        {
            // 全体が歪まないように正規化する
            float peak = 0f;
            foreach (var v in buffer) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f)
            {
                float scale = 0.85f / peak;
                for (int i = 0; i < buffer.Length; i++) buffer[i] *= scale;
            }

            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }

        /// <summary>タイトル画面用。明るくポップな8小節ループ。</summary>
        public static AudioClip Title()
        {
            const float bpm = 128f;
            var melody = new List<Step>
            {
                N(E5,.5f),N(G5,.5f),N(C6,1),N(G5,.5f),N(E5,.5f),N(D5,1),
                N(F5,.5f),N(A5,.5f),N(G5,1),N(E5,.5f),N(D5,.5f),N(C5,1),
                N(D5,.5f),N(E5,.5f),N(F5,1),N(E5,.5f),N(D5,.5f),N(G5,1),
                N(E5,.5f),N(C5,.5f),N(D5,1),N(E5,1),Rest(1),
                N(G5,.5f),N(A5,.5f),N(B5,1),N(C6,.5f),N(B5,.5f),N(A5,1),
                N(F5,.5f),N(A5,.5f),N(C6,1),N(B5,.5f),N(G5,.5f),N(E5,1),
                N(C6,.5f),N(B5,.5f),N(A5,1),N(G5,.5f),N(F5,.5f),N(E5,1),
                N(D5,.5f),N(E5,.5f),N(C5,2),Rest(1),
            };
            var bass = new List<Step>();
            int[] roots = { C3, A2, F2, G2, C3, A2, F2, G2 };
            foreach (var r in roots)
                for (int i = 0; i < 4; i++) { bass.Add(N(r, .5f)); bass.Add(Rest(.5f)); }

            var melodyAdsr = new Adsr(0.014f, 0.10f, 0.60f, 0.16f);
            var bassAdsr = new Adsr(0.008f, 0.14f, 0.45f, 0.12f);

            var buffer = new float[LengthInSamples(melody, bpm)];
            AddTrack(buffer, bpm, melody, melodyAdsr, 0.30f, 0.88f, 0.32f, 0.10f, chordGain: 0.28f);
            AddTrack(buffer, bpm, bass, bassAdsr, 0.24f, 0.70f, 0.45f, 0.18f);
            return Finish("BGM_Title", buffer);
        }

        /// <summary>プレイ中用。タイトルより速く、走る感じのループ。</summary>
        public static AudioClip Play()
        {
            const float bpm = 152f;
            var melody = new List<Step>
            {
                N(C5,.5f),N(E5,.5f),N(G5,.5f),N(E5,.5f),N(A5,.5f),N(G5,.5f),N(E5,1),
                N(D5,.5f),N(F5,.5f),N(A5,.5f),N(F5,.5f),N(G5,.5f),N(F5,.5f),N(D5,1),
                N(E5,.5f),N(G5,.5f),N(C6,.5f),N(G5,.5f),N(B5,.5f),N(A5,.5f),N(G5,1),
                N(F5,.5f),N(E5,.5f),N(D5,.5f),N(E5,.5f),N(C5,2),
                N(G5,.5f),N(B5,.5f),N(D5+12,.5f),N(B5,.5f),N(C6,.5f),N(B5,.5f),N(G5,1),
                N(F5,.5f),N(A5,.5f),N(C6,.5f),N(A5,.5f),N(B5,.5f),N(A5,.5f),N(F5,1),
                N(E5,.5f),N(G5,.5f),N(B5,.5f),N(G5,.5f),N(A5,.5f),N(G5,.5f),N(E5,1),
                N(D5,.5f),N(C5,.5f),N(D5,.5f),N(E5,.5f),N(C5,2),
            };
            var arp = new List<Step>();
            int[][] chords =
            {
                new[]{C4,E4,G4}, new[]{D4,F4,A4}, new[]{E4,G4,B4}, new[]{C4,E4,G4},
                new[]{G4,B4,D5}, new[]{F4,A4,C5}, new[]{E4,G4,B4}, new[]{C4,E4,G4},
            };
            foreach (var ch in chords)
                for (int i = 0; i < 8; i++) arp.Add(N(ch[i % 3], .5f));

            var bass = new List<Step>();
            int[] roots = { C2, D4 - 24, E2, C2, G2, F2, E2, C2 };
            foreach (var r in roots)
                for (int i = 0; i < 8; i++) bass.Add(N(r, .5f));

            var melodyAdsr = new Adsr(0.012f, 0.09f, 0.58f, 0.14f);
            var arpAdsr = new Adsr(0.005f, 0.06f, 0.30f, 0.07f);
            var bassAdsr = new Adsr(0.008f, 0.12f, 0.45f, 0.10f);

            var buffer = new float[LengthInSamples(melody, bpm)];
            AddTrack(buffer, bpm, melody, melodyAdsr, 0.30f, 0.85f, 0.30f, 0.10f, chordGain: 0.26f);
            AddTrack(buffer, bpm, arp, arpAdsr, 0.10f, 0.55f, 0.25f, 0.08f);
            AddTrack(buffer, bpm, bass, bassAdsr, 0.26f, 0.65f, 0.45f, 0.18f);
            return Finish("BGM_Play", buffer);
        }

        /// <summary>ゲームオーバー用。しんみりした短い下降。ループしない。</summary>
        public static AudioClip GameOver()
        {
            const float bpm = 96f;
            // 8拍 = 5.0秒。ゆっくり下りて終わる。
            var melody = new List<Step>
            {
                N(A4,1),N(G4,1),N(F4,1),N(E4,1),
                N(D4,1),N(C4,1),N(A4 - 12,2),
            };
            var bass = new List<Step>
            {
                N(A3 - 12,2),N(F3 - 12,2),
                N(G3 - 12,2),N(A3 - 24,2),
            };

            // ゆっくり立ち上がって長く残す。しんみりした余韻を出す。
            var slow = new Adsr(0.035f, 0.32f, 0.55f, 0.50f);
            var bassAdsr = new Adsr(0.025f, 0.30f, 0.50f, 0.45f);

            var buffer = new float[LengthInSamples(melody, bpm)];
            AddTrack(buffer, bpm, melody, slow, 0.40f, 0.95f, 0.26f, 0.09f, chordGain: 0.30f);
            AddTrack(buffer, bpm, bass, bassAdsr, 0.20f, 0.95f, 0.40f, 0.15f);
            return Finish("BGM_GameOver", buffer);
        }
    }
}
