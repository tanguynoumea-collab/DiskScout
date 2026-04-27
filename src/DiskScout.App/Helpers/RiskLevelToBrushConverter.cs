using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DiskScout.Models;

namespace DiskScout.Helpers;

/// <summary>
/// One-way <see cref="IValueConverter"/> turning a <see cref="RiskLevel"/>
/// into the SolidColorBrush used to paint the Score badge and the
/// "Pourquoi ?" diagnostics modal banner. Palette is aligned with the
/// per-tier semantics from <c>10-CONTEXT.md</c>:
///
///   <list type="bullet">
///     <item>Aucun    → green   (#27AE60) — safe to delete</item>
///     <item>Faible   → lime    (#2ECC71) — recoverable concern</item>
///     <item>Moyen    → orange  (#F39C12) — verify before</item>
///     <item>Eleve    → dark-orange (#E67E22) — never auto-propose</item>
///     <item>Critique → red     (#E74C3C) — never touch</item>
///   </list>
///
/// Returns <see cref="Brushes.Transparent"/> when the value is null
/// (non-AppData rows have no diagnostics so no badge color).
/// </summary>
public sealed class RiskLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush BrushAucun    = MakeFrozen(0x27, 0xAE, 0x60);
    private static readonly SolidColorBrush BrushFaible   = MakeFrozen(0x2E, 0xCC, 0x71);
    private static readonly SolidColorBrush BrushMoyen    = MakeFrozen(0xF3, 0x9C, 0x12);
    private static readonly SolidColorBrush BrushEleve    = MakeFrozen(0xE6, 0x7E, 0x22);
    private static readonly SolidColorBrush BrushCritique = MakeFrozen(0xE7, 0x4C, 0x3C);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            RiskLevel.Aucun    => BrushAucun,
            RiskLevel.Faible   => BrushFaible,
            RiskLevel.Moyen    => BrushMoyen,
            RiskLevel.Eleve    => BrushEleve,
            RiskLevel.Critique => BrushCritique,
            _                   => Brushes.Transparent,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException(
            $"{nameof(RiskLevelToBrushConverter)} is one-way only.");

    private static SolidColorBrush MakeFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
