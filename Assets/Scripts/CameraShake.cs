using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// カメラを短時間ゆらして手応えを出す。
    /// 揺れは時間とともに減衰し、終わったら必ず元の位置に戻す。
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraShake : MonoBehaviour
    {
        [Tooltip("連鎖1段あたりの揺れ幅（ワールド単位）")]
        [Min(0f)]
        [SerializeField] float strengthPerChain = 0.055f;
        [Tooltip("揺れ幅の上限。連鎖が伸びても暴れすぎないようにする")]
        [Min(0f)]
        [SerializeField] float maxStrength = 0.26f;
        [Tooltip("1回の揺れが収まるまでの秒数")]
        [Min(0.01f)]
        [SerializeField] float duration = 0.25f;
        [Tooltip("1秒あたりの揺れの回数。大きいほど細かく震える")]
        [Min(1f)]
        [SerializeField] float frequency = 26f;

        Vector3 basePosition;
        float elapsed;
        float strength;
        bool shaking;

        /// <summary>揺れている最中か。</summary>
        public bool IsShaking => shaking;

        /// <summary>直前に指定された揺れ幅（動作確認用）。</summary>
        public float CurrentStrength => strength;

        void OnDisable()
        {
            Stop();
        }

        void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 連鎖数に応じて揺らす。1連鎖以下では揺らさない。
        /// </summary>
        public void ShakeForChain(int chain)
        {
            if (chain < 2) return;
            Shake((chain - 1) * strengthPerChain);
        }

        /// <summary>指定した強さで揺らす。すでに揺れている場合は強い方を採用する。</summary>
        public void Shake(float amount)
        {
            amount = Mathf.Min(amount, maxStrength);
            if (amount <= 0f) return;

            if (!shaking) basePosition = transform.localPosition;
            strength = Mathf.Max(shaking ? strength : 0f, amount);
            elapsed = 0f;
            shaking = true;
        }

        /// <summary>時間を進める。LateUpdate から呼ばれるが、テストからも直接呼べる。</summary>
        public void Tick(float dt)
        {
            if (!shaking) return;

            elapsed += dt;
            if (elapsed >= duration)
            {
                Stop();
                return;
            }

            // 残り時間に比例して弱める
            float falloff = 1f - elapsed / duration;
            float amount = strength * falloff * falloff;

            // 正弦波を位相違いで組み合わせ、規則的すぎない揺れにする
            float t = elapsed * frequency;
            float x = Mathf.Sin(t) * amount;
            float y = Mathf.Sin(t * 1.37f + 1.7f) * amount * 0.85f;

            transform.localPosition = basePosition + new Vector3(x, y, 0f);
        }

        /// <summary>揺れを止めて元の位置に戻す。</summary>
        public void Stop()
        {
            if (!shaking) return;
            transform.localPosition = basePosition;
            shaking = false;
            strength = 0f;
            elapsed = 0f;
        }
    }
}
