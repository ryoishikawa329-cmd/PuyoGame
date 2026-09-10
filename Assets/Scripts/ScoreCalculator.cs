using System.Collections.Generic;

namespace PuyoGame
{
    /// <summary>
    /// 消去結果からスコアを計算する純粋なロジック。MonoBehaviour ではない。
    /// </summary>
    public static class ScoreCalculator
    {
        /// <summary>ブロック1個あたりの基本点。</summary>
        public const int DefaultPointsPerBlock = 10;

        /// <summary>倍率が際限なく膨らまないよう、指数はここで頭打ちにする。</summary>
        public const int MaxChainExponent = 20;

        /// <summary>
        /// 連鎖数に対する倍率。1連鎖=×1, 2連鎖=×2, 3連鎖=×4 … と倍々に増える。
        /// </summary>
        public static int ChainMultiplier(int chain)
        {
            if (chain <= 1) return 1;
            int exponent = chain - 1;
            if (exponent > MaxChainExponent) exponent = MaxChainExponent;
            return 1 << exponent;
        }

        /// <summary>連鎖1ステップ分の得点。消したマス数 × 基本点 × 連鎖倍率。</summary>
        public static int StepScore(int clearedCount, int chain, int pointsPerBlock = DefaultPointsPerBlock)
        {
            if (clearedCount <= 0) return 0;
            return clearedCount * pointsPerBlock * ChainMultiplier(chain);
        }

        /// <summary>
        /// 連鎖全体の得点。clearedPerChain[i] は (i+1) 連鎖目で消えたマス数。
        /// </summary>
        public static int TotalScore(IReadOnlyList<int> clearedPerChain, int pointsPerBlock = DefaultPointsPerBlock)
        {
            if (clearedPerChain == null) return 0;

            int total = 0;
            for (int i = 0; i < clearedPerChain.Count; i++)
                total += StepScore(clearedPerChain[i], i + 1, pointsPerBlock);
            return total;
        }
    }
}
