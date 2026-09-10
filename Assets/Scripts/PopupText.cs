using TMPro;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 画面中央に短くテキストを出して、拡大しながら消える演出。
    /// 大量消去したときの手応えを出すために使う。
    /// </summary>
    [DisallowMultipleComponent]
    public class PopupText : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [Tooltip("出てから消えるまでの秒数")]
        [Min(0.05f)]
        [SerializeField] float duration = 0.9f;
        [Tooltip("表示開始時の大きさ（1が等倍）")]
        [SerializeField] float startScale = 0.6f;
        [Tooltip("消えるまでに広がる大きさ")]
        [SerializeField] float endScale = 1.15f;
        [Tooltip("この割合を過ぎてから薄くなり始める")]
        [Range(0f, 1f)]
        [SerializeField] float fadeStart = 0.45f;

        float elapsed;
        bool playing;
        Color baseColor = Color.white;

        /// <summary>再生中か。</summary>
        public bool IsPlaying => playing;

        /// <summary>現在表示している文字列（動作確認用）。</summary>
        public string CurrentText => label != null ? label.text : string.Empty;

        void Awake()
        {
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            Hide();
        }

        void OnDisable()
        {
            Hide();
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>テキストを出す。すでに出ていれば上書きして最初から再生する。</summary>
        public void Show(string text, Color color)
        {
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            baseColor = color;
            label.text = text;
            label.gameObject.SetActive(true);
            elapsed = 0f;
            playing = true;
            Apply(0f);
        }

        /// <summary>時間を進める。Update から呼ばれるが、テストからも直接呼べる。</summary>
        public void Tick(float dt)
        {
            if (!playing) return;

            elapsed += dt;
            if (elapsed >= duration)
            {
                Hide();
                return;
            }
            Apply(elapsed / duration);
        }

        void Apply(float progress)
        {
            if (label == null) return;

            // 出だしだけ勢いよく広がるように、進みを緩める
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            label.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);

            float alpha = progress < fadeStart
                ? 1f
                : 1f - (progress - fadeStart) / (1f - fadeStart);
            var c = baseColor;
            c.a = Mathf.Clamp01(alpha);
            label.color = c;
        }

        /// <summary>すぐに消す。</summary>
        public void Hide()
        {
            playing = false;
            elapsed = 0f;
            if (label != null) label.gameObject.SetActive(false);
        }
    }
}
