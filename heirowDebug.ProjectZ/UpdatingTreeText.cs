using System.Diagnostics;
using Microsoft.Xna.Framework;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI.Primitives;

namespace heirowDebug.ProjectZ;

/// <summary>A retained tree label whose glyph shadow fades after a recorded change.</summary>
internal sealed class UpdatingTreeText : TextElement
{
    readonly XamlNativeEffect glow = new()
    {
        Kind = XamlEffectKind.DropShadow, Radius = 5, ShadowDepth = 0,
        Color = new Color(45, 255, 105), Opacity = 0
    };
    internal const double FadeSeconds = 1;
    internal DebugValue? Value { get; set; }
    internal float GlowOpacity => glow.Opacity;

    public UpdatingTreeText(Scene scene) : base(scene)
    {
        // SceneElement owns and disposes the effect. Zero opacity bypasses its
        // render targets entirely once the animation has finished.
        VisualEffect = glow;
    }

    internal void AnimateUpdate(long now)
    {
        long changed = isVisible ? Value?.LastChangedTimestamp ?? 0 : 0;
        double remaining = changed == 0 ? 0 : Math.Clamp(1 - Stopwatch.GetElapsedTime(changed, now).TotalSeconds / FadeSeconds, 0, 1);
        glow.Opacity = (float)(.9 * remaining * remaining);
    }
}
