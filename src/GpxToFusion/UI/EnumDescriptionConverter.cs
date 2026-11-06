using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace GpxToFusion.UI;

/// <summary>Shows [Description] texts of enum members in the PropertyGrid.</summary>
public sealed class EnumDescriptionConverter<T> : EnumConverter where T : struct, Enum
{
    public EnumDescriptionConverter() : base(typeof(T)) { }

    private static string Describe(T value) =>
        typeof(T).GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is T t ? Describe(t) : base.ConvertTo(context, culture, value, destinationType);

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
            foreach (var v in Enum.GetValues<T>())
                if (Describe(v) == s || v.ToString() == s) return v;
        return base.ConvertFrom(context, culture, value);
    }
}
