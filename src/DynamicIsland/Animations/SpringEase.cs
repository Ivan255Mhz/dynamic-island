using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.Animations;

/// <summary>
/// A damped-spring easing function that mimics the physics-based interpolation
/// used by iOS. It produces a smooth settle with an optional mild overshoot.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    /// <summary>Damping ratio. 1 = critically damped (no overshoot), &lt;1 overshoots.</summary>
    public double Damping { get; set; } = 0.72;

    /// <summary>Angular frequency of the spring (higher = snappier).</summary>
    public double Frequency { get; set; } = 10.0;

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        var zeta = Math.Clamp(Damping, 0.05, 1.5);
        var omega = Math.Max(0.1, Frequency);

        if (zeta >= 1)
        {
            // Critically damped: no overshoot, smooth settle.
            return 1 - ((1 + (omega * normalizedTime)) * Math.Exp(-omega * normalizedTime));
        }

        var dampedOmega = omega * Math.Sqrt(1 - (zeta * zeta));
        var decay = Math.Exp(-zeta * omega * normalizedTime);

        return 1 - (decay * (Math.Cos(dampedOmega * normalizedTime)
                             + ((zeta * omega / dampedOmega) * Math.Sin(dampedOmega * normalizedTime))));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
