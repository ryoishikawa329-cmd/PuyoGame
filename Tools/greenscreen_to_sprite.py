#!/usr/bin/env python3
"""クロマキーグリーンの背景を透明化して、ゲーム用のPNGスプライトに変換する。

使い方:
    python3 Tools/greenscreen_to_sprite.py <入力画像> -o <出力PNG> [--variants]

--variants を付けると、色相をずらして Red/Green/Blue/Yellow/Purple の5色版を書き出す。
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

# ぷよの5色に対応する目標色相（0-1）。BoardView の配色に合わせている。
VARIANT_HUES = {
    "Red": 0.00,
    "Green": 0.33,
    "Blue": 0.60,
    "Yellow": 0.13,
    "Purple": 0.78,
}


def build_alpha(rgb, key_low, key_high):
    """緑らしさから不透明度を作る。緑が強いほど透明にする。"""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # 緑が赤・青より突出している度合い。純緑で最大になる。
    greenness = g - np.maximum(r, b)
    alpha = 1.0 - np.clip((greenness - key_low) / (key_high - key_low), 0.0, 1.0)
    return alpha


def despill(rgb, strength=1.0):
    """輪郭に残る緑かぶりを抑える。緑チャンネルを他チャンネル基準まで下げる。"""
    r, g, b = rgb[..., 0].copy(), rgb[..., 1].copy(), rgb[..., 2].copy()
    limit = np.maximum(r, b)
    over = np.maximum(g - limit, 0.0)
    g = g - over * strength
    return np.stack([r, g, b], axis=-1)


def border_connected_background(alpha, threshold=0.5, grow=2):
    """外周から連結している透明域だけを「背景」とみなすマスクを返す。

    被写体の中に背景と同じ色（緑のキャラクターなど）があっても、
    外周とつながっていなければ残せる。
    """
    try:
        from scipy import ndimage
    except ImportError:
        return None

    transparent = alpha < threshold
    labels, n = ndimage.label(transparent)
    if n == 0:
        return None

    border = np.concatenate([
        labels[0, :], labels[-1, :], labels[:, 0], labels[:, -1],
    ])
    keep = np.unique(border)
    keep = keep[keep > 0]
    if keep.size == 0:
        return None

    mask = np.isin(labels, keep)
    if grow > 0:
        mask = ndimage.binary_dilation(mask, iterations=grow)
    return mask


def convert(path, key_low, key_high, spill, trim, square):
    src = Image.open(path).convert("RGB")
    rgb = np.asarray(src).astype(np.float64) / 255.0

    alpha = build_alpha(rgb, key_low, key_high)

    # 外周とつながっていない透明域は、被写体の中の緑なので不透明に戻す
    bg = border_connected_background(alpha)
    if bg is not None:
        alpha = np.where(bg, alpha, 1.0)
        # 緑かぶりの除去も背景の周辺だけに限定する
        near = bg
        rgb = np.where(near[..., None], despill(rgb, spill), rgb)
    else:
        rgb = despill(rgb, spill)

    rgba = np.concatenate([np.clip(rgb, 0, 1), alpha[..., None]], axis=-1)
    out = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")

    if trim:
        bbox = out.getbbox()
        if bbox:
            out = out.crop(bbox)
    if square:
        w, h = out.size
        side = max(w, h)
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(out, ((side - w) // 2, (side - h) // 2))
        out = canvas
    return out


def shift_hue(image, target_hue):
    """色相を目標値に寄せた色違いを作る。明度と彩度は保つ。"""
    import colorsys

    arr = np.asarray(image).astype(np.float64) / 255.0
    rgb, a = arr[..., :3], arr[..., 3]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)

    # 元の色相は捨て、目標色相で塗り直す（キャラの陰影は明度・彩度が担う）
    h = np.full_like(v, target_hue)
    flat = np.stack([h.ravel(), s.ravel(), v.ravel()], axis=-1)
    conv = np.array([colorsys.hsv_to_rgb(*px) for px in flat]).reshape(rgb.shape)

    out = np.concatenate([np.clip(conv, 0, 1), a[..., None]], axis=-1)
    return Image.fromarray((out * 255).astype(np.uint8), "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("-o", "--output", required=True)
    ap.add_argument("--key-low", type=float, default=0.06,
                    help="これ以下の緑らしさは完全不透明のまま")
    ap.add_argument("--key-high", type=float, default=0.30,
                    help="これ以上の緑らしさは完全透明にする")
    ap.add_argument("--spill", type=float, default=1.0, help="緑かぶり除去の強さ 0-1")
    ap.add_argument("--no-trim", action="store_true", help="余白の切り詰めをしない")
    ap.add_argument("--no-square", action="store_true", help="正方形化しない")
    ap.add_argument("--variants", action="store_true", help="5色版も書き出す")
    args = ap.parse_args()

    if not os.path.exists(args.input):
        print(f"入力が見つかりません: {args.input}", file=sys.stderr)
        return 1

    img = convert(args.input, args.key_low, args.key_high, args.spill,
                  not args.no_trim, not args.no_square)
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    img.save(args.output)

    a = np.asarray(img)[..., 3]
    print(f"書き出し: {args.output} {img.size} "
          f"透明{(a == 0).mean() * 100:.1f}% 半透明{((a > 0) & (a < 255)).mean() * 100:.1f}%")

    if args.variants:
        base, ext = os.path.splitext(args.output)
        for name, hue in VARIANT_HUES.items():
            v = shift_hue(img, hue)
            p = f"{base}_{name}{ext}"
            v.save(p)
            print(f"  色違い: {p}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
