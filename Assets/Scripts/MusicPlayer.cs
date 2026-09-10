using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// BGM の再生と切り替えを担当する。
    /// AudioSource を2つ使い、前の曲を薄くしながら次の曲を重ねて入れ替える。
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicPlayer : MonoBehaviour
    {
        /// <summary>曲の種類。</summary>
        public enum Track
        {
            None,
            Title,
            Play,
            GameOver,
        }

        [Header("音量")]
        [Range(0f, 1f)]
        [SerializeField] float volume = 0.35f;

        [Header("切り替え")]
        [Tooltip("前の曲が消えるまでの秒数")]
        [Min(0f)]
        [SerializeField] float fadeOutSeconds = 0.45f;
        [Tooltip("次の曲が立ち上がるまでの秒数")]
        [Min(0f)]
        [SerializeField] float fadeInSeconds = 0.55f;

        AudioSource sourceA, sourceB;
        AudioSource current, previous;
        AudioClip titleClip, playClip, gameOverClip;

        float fadeInProgress, fadeOutProgress;
        float previousStartVolume;

        /// <summary>いま鳴らそうとしている曲。</summary>
        public Track CurrentTrack { get; private set; } = Track.None;

        /// <summary>切り替え中かどうか（動作確認用）。</summary>
        public bool IsFading => (current != null && fadeInProgress < 1f)
                             || (previous != null && fadeOutProgress < 1f);

        /// <summary>いま鳴っている音量（動作確認用）。</summary>
        public float CurrentVolume => current != null ? current.volume : 0f;
        public float PreviousVolume => previous != null ? previous.volume : 0f;

        void Awake()
        {
            EnsureSources();
        }

        void OnDestroy()
        {
            DestroyClip(titleClip);
            DestroyClip(playClip);
            DestroyClip(gameOverClip);
        }

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        void EnsureSources()
        {
            if (sourceA != null && sourceB != null) return;

            var sources = GetComponents<AudioSource>();
            sourceA = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            sourceB = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();

            foreach (var s in new[] { sourceA, sourceB })
            {
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.volume = 0f;
            }
        }

        /// <summary>指定の曲に切り替える。同じ曲なら何もしない。</summary>
        public void PlayTrack(Track track)
        {
            if (track == CurrentTrack) return;
            CurrentTrack = track;

            if (track == Track.None)
            {
                FadeOutCurrent();
                return;
            }

            var clip = ClipFor(track);
            if (clip == null) return;

            EnsureSources();
            FadeOutCurrent();

            // 空いている方に次の曲を入れる
            current = (previous == sourceA) ? sourceB : sourceA;
            current.clip = clip;
            current.loop = track != Track.GameOver;    // ゲームオーバーは鳴らし切り
            current.volume = 0f;
            current.Play();
            fadeInProgress = fadeInSeconds > 0f ? 0f : 1f;
        }

        void FadeOutCurrent()
        {
            if (current == null) return;
            previous = current;
            previousStartVolume = previous.volume;
            fadeOutProgress = fadeOutSeconds > 0f ? 0f : 1f;
            current = null;
        }

        /// <summary>時間を進める。Update から呼ばれるが、テストからも直接呼べる。</summary>
        public void Tick(float dt)
        {
            if (previous != null)
            {
                fadeOutProgress = fadeOutSeconds > 0f
                    ? Mathf.Min(1f, fadeOutProgress + dt / fadeOutSeconds) : 1f;
                previous.volume = Mathf.Lerp(previousStartVolume, 0f, fadeOutProgress);
                if (fadeOutProgress >= 1f)
                {
                    previous.Stop();
                    previous.clip = null;
                    previous = null;
                }
            }

            if (current != null && fadeInProgress < 1f)
            {
                fadeInProgress = fadeInSeconds > 0f
                    ? Mathf.Min(1f, fadeInProgress + dt / fadeInSeconds) : 1f;
                current.volume = Mathf.Lerp(0f, volume, fadeInProgress);
            }
        }

        /// <summary>すべて止める。</summary>
        public void StopAll()
        {
            CurrentTrack = Track.None;
            foreach (var s in new[] { sourceA, sourceB })
            {
                if (s == null) continue;
                s.Stop();
                s.clip = null;
                s.volume = 0f;
            }
            current = null;
            previous = null;
        }

        /// <summary>曲を取り出す。初回だけ合成して以降は使い回す。</summary>
        public AudioClip ClipFor(Track track)
        {
            switch (track)
            {
                case Track.Title:
                    return titleClip != null ? titleClip : (titleClip = MusicSynth.Title());
                case Track.Play:
                    return playClip != null ? playClip : (playClip = MusicSynth.Play());
                case Track.GameOver:
                    return gameOverClip != null ? gameOverClip : (gameOverClip = MusicSynth.GameOver());
                default:
                    return null;
            }
        }

        static void DestroyClip(AudioClip clip)
        {
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
        }
    }
}
