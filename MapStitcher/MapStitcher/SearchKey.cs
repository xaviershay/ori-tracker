using System;
using System.ComponentModel;
using System.Globalization;

namespace MapStitcher
{
    [TypeConverter(typeof(SearchKeyConverter))]
    internal class SearchKey : Tuple<string, NeedleKey>
    {
        public SearchKey(string item1, NeedleKey item2) : base(item1, item2)
        {
        }

        public static SearchKey Create(string item1, NeedleKey item2)
        {
            return new SearchKey(item1, item2);
        }

        private class SearchKeyConverter : TypeConverter
        {
            private TypeConverter converter = new NeedleKey.NeedleKeyConverter();

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var x = (string)value;
                var tokens = x.Split(new char[] { '|' }, 2);

                if (tokens.Length != 2)
                {
                    throw new ArgumentException("Tuple serialization must have two tokens separated by |");

                }

                string item1 = tokens[0];
                NeedleKey item2 = (NeedleKey)(converter.ConvertFrom(context, culture, tokens[1]));

                return SearchKey.Create(item1, item2);
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string))
                {
                    var key = (Tuple<string, NeedleKey>)value;

                    return $"{key.Item1}|{converter.ConvertTo(context, culture, key.Item2, destinationType)}";
                } else
                {
                    throw new ArgumentException("Can't convert to anything but string");
                }
            }
        }

    }
}