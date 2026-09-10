using System.Collections.Generic;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 効果音の生成と再生をまとめる。音声ファイルは持たず、すべてその場で合成する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>効果音の種類。</summary>
        public enum Sound
        {
            Move,
            Rotate,
            Land,
            Clear,
            Chain,
            GameOver,
            Confirm,
        }

        [Header("音量")]
        [Range(0f, 1f)] [SerializeField] float masterVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] float moveVolume = 0.35f;
        [Range(0f, 1f)] [SerializeField] float rotateVolume = 0.40f;
        [Range(0f, 1f)] [SerializeField] float landVolume = 0.55f;
        [Range(0f, 1f)] [SerializeField] float clearVolume = 0.70f;
        [Range(0f, 1f)] [SerializeField] float chainVolume = 0.75f;
        [Range(0f, 1f)] [SerializeField] float gameOverVolume = 0.65f;
        [Range(0f, 1f)] [SerializeField] float confirmVolume = 0.60f;

        [Header("動作")]
        [Tooltip("同じ音が短時間に重なりすぎないよう、最小の間隔を空ける（秒）")]
        [Min(0f)]
        [SerializeField] float minInterval = 0.02f;

        AudioSource source;
        AudioClip move, rotate, land, gameOver, confirm;
        readonly Dictionary<int, AudioClip> clearClips = new Dictionary<int, AudioClip>();
        readonly Dictionary<int, AudioClip> chainClips = new Dictionary<int, AudioClip>();
        readonly Dictionary<Sound, float> lastPlayed = new Dictionary<Sound, float>();

        /// <summary>種類ごとの再生回数（動作確認用）。</summary>
        public readonly Dictionary<Sound, int> PlayCount = new Dictionary<Sound, int>();

        /// <summary>直前に再生したクリップ（動作確認用）。</summary>
        public AudioClip LastClip { get; private set; }

        void Awake()
        {
            EnsureSource();
        }

        void OnDestroy()
        {
            DestroyClip(move); DestroyClip(rotate); DestroyClip(land);
            DestroyClip(gameOver); DestroyClip(confirm);
            foreach (var c in clearClips.Values) DestroyClip(c);
            foreach (var c in chainClips.Values) DestroyClip(c);
            clearClips.Clear();
            chainClips.Clear();
        }

        void EnsureSource()
        {
            if (source != null) return;
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;      // 位置に関係なく鳴らす
            source.loop = false;
        }

        public void PlayMove() => Play(Sound.Move, MoveClip(), moveVolume);
        public void PlayRotate() => Play(Sound.Rotate, RotateClip(), rotateVolume);
        public void PlayLand() => Play(Sound.Land, LandClip(), landVolume);
        public void PlayGameOver() => Play(Sound.GameOver, GameOverClip(), gameOverVolume);
        public void PlayConfirm() => Play(Sound.Confirm, ConfirmClip(), confirmVolume);

        /// <summary>消去音。消した数が多いほど高くなる。</summary>
        public void PlayClear(int clearedCount) =>
            Play(Sound.Clear, ClearClip(clearedCount), clearVolume);

        /// <summary>連鎖音。連鎖数が多いほど高くなる。</summary>
        public void PlayChain(int chain) =>
            Play(Sound.Chain, ChainClip(chain), chainVolume);

        void Play(Sound kind, AudioClip clip, float volume)
        {
            if (clip == null) return;

            // 連打で音が潰れないよう、直前と近すぎる場合は鳴らさない
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(kind, out var prev) && now - prev < minInterval) return;
            lastPlayed[kind] = now;

            PlayCount[kind] = PlayCount.TryGetValue(kind, out var n) ? n + 1 : 1;
            LastClip = clip;

            EnsureSource();
            if (source != null) source.PlayOneShot(clip, Mathf.Clamp01(masterVolume * volume));
        }

        // ---------------- クリップの生成（初回のみ作って使い回す） ----------------

        public AudioClip MoveClip() => move != null ? move : (move = SoundSynth.Move());
        public AudioClip RotateClip() => rotate != null ? rotate : (rotate = SoundSynth.Rotate());
        public AudioClip LandClip() => land != null ? land : (land = SoundSynth.Land());
        public AudioClip GameOverClip() => gameOver != null ? gameOver : (gameOver = SoundSynth.GameOver());
        public AudioClip ConfirmClip() => confirm != null ? confirm : (confirm = SoundSynth.Confirm());

        public AudioClip ClearClip(int clearedCount)
        {
            int key = Mathf.Clamp(clearedCount, 4, 16);
            if (clearClips.TryGetValue(key, out var c) && c != null) return c;
            c = SoundSynth.Clear(key);
            clearClips[key] = c;
            return c;
        }

        public AudioClip ChainClip(int chain)
        {
            int key = Mathf.Clamp(chain, 1, 11);
            if (chainClips.TryGetValue(key, out var c) && c != null) return c;
            c = SoundSynth.Chain(key);
            chainClips[key] = c;
            return c;
        }

        static void DestroyClip(AudioClip clip)
        {
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
        }
    }
}
