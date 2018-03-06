using ImageMagick;
using System;
using System.ComponentModel;
using System.Globalization;

namespace MapStitcher
{
    [TypeConverter(typeof(NeedleKeyConverter))]
    public struct NeedleKey
    {
        public string Key;
        public Gravity Gravity;

        public override string ToString()
        {
            return $"{System.IO.Path.GetFileName(Key)}|{Gravity}";
        }

        internal class NeedleKeyConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var x = (string)value;

                var tokens = x.Split('|');

                if (tokens.Length != 2)
                {
                    throw new ArgumentException("NeedleKey serialization must have two tokens separated by |");
                }

                Gravity g = (Gravity)Enum.Parse(typeof(Gravity), tokens[1]);

                return new NeedleKey { Key = tokens[0], Gravity = g};
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string))
                {
                    var key = (NeedleKey)value;

                    if (key.Key.IndexOf("|") >= 0)
                    {
                        throw new ArgumentException("Cannot convert NeedleKey with | character in Key");
                    }
                    return $"{key.Key}|{key.Gravity}";
                } else
                {
                    throw new ArgumentException("Can't convert NeedleKey to anything but string");
                }
            }
        }
    }
}
