using System;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 効果音をその場で合成する。音声ファイルを持たずに済ませるための最小限の波形生成。
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 44100;

        /// <summary>時刻を受け取って振幅を返す関数から AudioClip を作る。</summary>
        public static AudioClip Build(string name, float duration, Func<float, float> sample)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(duration * SampleRate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(sample(i / (float)SampleRate), -1f, 1f);

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>立ち上がりが速く、あとは減衰していく包絡線。</summary>
        public static float Envelope(float t, float duration, float attack = 0.004f, float power = 3f)
        {
            if (t < 0f || t > duration) return 0f;
            if (attack > 0f && t < attack) return t / attack;

            float rest = Mathf.Max(duration - attack, 1e-4f);
            // 丸め誤差で底がわずかに負になると、非整数乗が NaN を返してしまう
            float remain = Mathf.Max(0f, 1f - (t - attack) / rest);
            return Mathf.Pow(remain, power);
        }

        /// <summary>
        /// 周波数が f0 から f1 へ直線的に変わるときの位相。
        /// 周波数×時刻では波が飛ぶので、周波数を積分して求める。
        /// </summary>
        public static float SweepPhase(float t, float f0, float f1, float duration)
        {
            float k = (f1 - f0) / Mathf.Max(duration, 1e-4f);
            return 2f * Mathf.PI * (f0 * t + 0.5f * k * t * t);
        }

        public static float Sine(float phase) => Mathf.Sin(phase);

        /// <summary>のこぎり波。サイン波より硬い音になる。</summary>
        public static float Saw(float phase)
        {
            float p = phase / (2f * Mathf.PI);
            return 2f * (p - Mathf.Floor(p + 0.5f));
        }

        /// <summary>矩形波。</summary>
        public static float Square(float phase) => Mathf.Sin(phase) >= 0f ? 1f : -1f;

        static readonly System.Random Rng = new System.Random(20260910);

        /// <summary>ざらつきを足すための雑音。</summary>
        public static float Noise() => (float)(Rng.NextDouble() * 2.0 - 1.0);

        // ---------------- 効果音 ----------------

        /// <summary>移動音：短く軽い「ピッ」。</summary>
        public static AudioClip Move()
        {
            const float d = 0.05f;
            return Build("SE_Move", d, t =>
                Sine(SweepPhase(t, 1250f, 1180f, d)) * Envelope(t, d, 0.002f, 3.5f) * 0.5f);
        }

        /// <summary>回転音：移動音より低い「クッ」。</summary>
        public static AudioClip Rotate()
        {
            const float d = 0.05f;
            return Build("SE_Rotate", d, t =>
            {
                float body = Square(SweepPhase(t, 700f, 640f, d)) * 0.35f;
                float soft = Sine(SweepPhase(t, 700f, 640f, d)) * 0.4f;
                return (body + soft) * Envelope(t, d, 0.002f, 4f) * 0.55f;
            });
        }

        /// <summary>着地音：短く鈍い「ポトッ」。</summary>
        public static AudioClip Land()
        {
            const float d = 0.10f;
            return Build("SE_Land", d, t =>
            {
                // 低い音を急に下げて、当たった感じを出す
                float body = Sine(SweepPhase(t, 220f, 110f, d)) * 0.8f;
                float click = Noise() * Envelope(t, 0.012f, 0.001f, 2f) * 0.25f;
                return (body * Envelope(t, d, 0.003f, 2.5f) + click) * 0.7f;
            });
        }

        /// <summary>消去音：弾ける「ポン」。消した数が多いほど少し高くなる。</summary>
        public static AudioClip Clear(int clearedCount)
        {
            const float d = 0.20f;
            // 4個を基準に、増えるほど半音ずつ上げる（上げ幅は頭打ち）
            int steps = Mathf.Clamp(clearedCount - 4, 0, 12);
            float scale = Mathf.Pow(1.0595f, steps);
            float f0 = 420f * scale, f1 = 760f * scale;

            return Build($"SE_Clear_{clearedCount}", d, t =>
            {
                float main = Sine(SweepPhase(t, f0, f1, d * 0.5f));
                float sub = Sine(SweepPhase(t, f0 * 2f, f1 * 2f, d * 0.5f)) * 0.3f;
                return (main + sub) * Envelope(t, d, 0.004f, 2.6f) * 0.55f;
            });
        }

        /// <summary>連鎖音：華やかな上昇音「キラーン」。連鎖数で高くなる。</summary>
        public static AudioClip Chain(int chain)
        {
            const float d = 0.30f;
            int steps = Mathf.Clamp(chain - 1, 0, 10);
            float scale = Mathf.Pow(1.0595f, steps * 2);      // 連鎖ごとに全音上げる
            float f0 = 620f * scale, f1 = 1500f * scale;

            return Build($"SE_Chain_{chain}", d, t =>
            {
                float lead = Sine(SweepPhase(t, f0, f1, d));
                float shine = Sine(SweepPhase(t, f0 * 2f, f1 * 2f, d)) * 0.45f;
                float sparkle = Sine(SweepPhase(t, f0 * 3f, f1 * 3f, d)) * 0.2f;
                return (lead + shine + sparkle) * Envelope(t, d, 0.006f, 1.8f) * 0.45f;
            });
        }

        /// <summary>決定音：短く明るい「ピロン」。ボタンを押したときに使う。</summary>
        public static AudioClip Confirm()
        {
            const float d = 0.13f;
            return Build("SE_Confirm", d, t =>
            {
                // 高い音を2つ重ね、後半で上に跳ねさせる
                float lead = Sine(SweepPhase(t, 880f, 1320f, d));
                float top = Sine(SweepPhase(t, 1320f, 1760f, d)) * 0.5f;
                float shine = Square(SweepPhase(t, 1760f, 2640f, d)) * 0.12f;
                return (lead + top + shine) * Envelope(t, d, 0.004f, 2.2f) * 0.5f;
            });
        }

        /// <summary>ゲームオーバー音：寂しい下降音。</summary>
        public static AudioClip GameOver()
        {
            const float d = 0.50f;
            return Build("SE_GameOver", d, t =>
            {
                float lead = Saw(SweepPhase(t, 440f, 130f, d)) * 0.35f;
                float body = Sine(SweepPhase(t, 440f, 130f, d)) * 0.6f;
                return (lead + body) * Envelope(t, d, 0.010f, 1.4f) * 0.5f;
            });
        }
    }
}
