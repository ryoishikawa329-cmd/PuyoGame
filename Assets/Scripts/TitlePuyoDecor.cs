using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// タイトル画面の写真ぷよを、ふわふわと漂わせる。
    /// 置き場所は子オブジェクト側が持っていて、ここは基準位置からの揺らしだけを受け持つ。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitlePuyoDecor : MonoBehaviour
    {
        [Tooltip("上下の振れ幅（px）の基準。写真ごとに少しずつ変える")]
        [Min(0f)]
        [SerializeField] float verticalAmplitude = 48f;
        [Tooltip("左右の振れ幅（px）の基準")]
        [Min(0f)]
        [SerializeField] float horizontalAmplitude = 32f;
        [Tooltip("1往復にかかる秒数の基準")]
        [Min(0.1f)]
        [SerializeField] float period = 4.2f;
        [Tooltip("ゆっくり傾く角度（度）")]
        [Min(0f)]
        [SerializeField] float tilt = 7f;

        /// <summary>漂わせる対象1つ分の、基準位置と揺れ方。</summary>
        struct Floater
        {
            public RectTransform rt;
            public Vector2 home;
            public float ampX, ampY;
            public float periodX, periodY;
            public float phaseX, phaseY;
            public float tiltAngle, tiltPeriod, tiltPhase;
        }

        Floater[] floaters;

        /// <summary>並んでいる写真ぷよの数。</summary>
        public int FloaterCount => floaters != null ? floaters.Length : 0;

        void OnEnable() => Collect();

        void OnDisable() => ResetToHome();

        void Update() => Apply(Time.time);

        /// <summary>子オブジェクトを集めて、それぞれの揺れ方を決める。</summary>
        public void Collect()
        {
            int count = transform.childCount;
            floaters = new Floater[count];
            for (int i = 0; i < count; i++)
            {
                var rt = transform.GetChild(i) as RectTransform;
                if (rt == null) continue;

                // 添字から決めるので、実行するたびに同じ動きになる
                floaters[i] = new Floater
                {
                    rt = rt,
                    home = rt.anchoredPosition,
                    ampY = verticalAmplitude * (0.70f + Hash01(i, 1) * 0.80f),
                    ampX = horizontalAmplitude * (0.60f + Hash01(i, 2) * 0.90f),
                    periodY = period * (0.75f + Hash01(i, 3) * 0.70f),
                    // 縦と横で周期をずらすと、往復ではなく漂う軌道になる
                    periodX = period * (1.30f + Hash01(i, 4) * 0.90f),
                    phaseY = Hash01(i, 5) * Mathf.PI * 2f,
                    phaseX = Hash01(i, 6) * Mathf.PI * 2f,
                    tiltAngle = tilt * (0.50f + Hash01(i, 7)),
                    tiltPeriod = period * (1.6f + Hash01(i, 8) * 0.8f),
                    tiltPhase = Hash01(i, 9) * Mathf.PI * 2f,
                };
            }
        }

        /// <summary>指定の時刻での位置と傾きを反映する。</summary>
        public void Apply(float time)
        {
            if (floaters == null || floaters.Length != transform.childCount) Collect();

            for (int i = 0; i < floaters.Length; i++)
            {
                var f = floaters[i];
                if (f.rt == null) continue;

                float x = f.ampX * Mathf.Sin(Mathf.PI * 2f * time / f.periodX + f.phaseX);
                float y = f.ampY * Mathf.Sin(Mathf.PI * 2f * time / f.periodY + f.phaseY);
                f.rt.anchoredPosition = f.home + new Vector2(x, y);

                float angle = f.tiltAngle * Mathf.Sin(Mathf.PI * 2f * time / f.tiltPeriod + f.tiltPhase);
                f.rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        /// <summary>基準位置に戻す。次に表示したとき、ずれが積み重ならないようにする。</summary>
        public void ResetToHome()
        {
            if (floaters == null) return;
            foreach (var f in floaters)
            {
                if (f.rt == null) continue;
                f.rt.anchoredPosition = f.home;
                f.rt.localRotation = Quaternion.identity;
            }
        }

        /// <summary>添字から 0〜1 の値を作る。写真ごとに動きを変えるために使う。</summary>
        static float Hash01(int index, int salt)
        {
            float v = Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f;
            return Mathf.Abs(v - Mathf.Floor(v));
        }
    }
}
