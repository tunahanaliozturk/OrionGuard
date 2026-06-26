#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Moongazing.OrionGuard.OpenApi.Json
{
    /// <summary>
    /// Thrown when <see cref="JsonParser"/> encounters malformed JSON. The generator catches this and
    /// raises diagnostic OG1002 rather than letting the build crash.
    /// </summary>
    internal sealed class JsonParseException : Exception
    {
        public JsonParseException(string message) : base(message) { }
    }

    /// <summary>
    /// A minimal recursive-descent JSON parser with no external dependency. It is intentionally not a
    /// full JSON validator: it covers the grammar OpenAPI documents use (RFC 8259 objects, arrays,
    /// strings with the standard escapes, numbers, <c>true</c>/<c>false</c>/<c>null</c>) and rejects
    /// anything malformed with a <see cref="JsonParseException"/>. It is allocation-light enough for
    /// the document sizes a schema reference implies and runs entirely inside the analyzer.
    /// </summary>
    internal static class JsonParser
    {
        public static JsonValue Parse(string text)
        {
            if (text is null)
            {
                throw new JsonParseException("Document is null.");
            }

            var cursor = new Cursor(text);
            cursor.SkipWhitespace();
            var value = ParseValue(ref cursor);
            cursor.SkipWhitespace();

            if (!cursor.AtEnd)
            {
                throw new JsonParseException($"Unexpected trailing content at position {cursor.Position}.");
            }

            return value;
        }

        private static JsonValue ParseValue(ref Cursor cursor)
        {
            cursor.SkipWhitespace();
            if (cursor.AtEnd)
            {
                throw new JsonParseException("Unexpected end of document while expecting a value.");
            }

            char c = cursor.Current;
            switch (c)
            {
                case '{':
                    return ParseObject(ref cursor);
                case '[':
                    return ParseArray(ref cursor);
                case '"':
                    return JsonValue.NewString(ParseString(ref cursor));
                case 't':
                case 'f':
                    return ParseBoolean(ref cursor);
                case 'n':
                    ParseLiteral(ref cursor, "null");
                    return JsonValue.NewNull();
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber(ref cursor);
                    }

                    throw new JsonParseException($"Unexpected character '{c}' at position {cursor.Position}.");
            }
        }

        private static JsonValue ParseObject(ref Cursor cursor)
        {
            cursor.Expect('{');
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            cursor.SkipWhitespace();
            if (cursor.TryConsume('}'))
            {
                return JsonValue.NewObject(members);
            }

            while (true)
            {
                cursor.SkipWhitespace();
                if (cursor.AtEnd)
                {
                    throw new JsonParseException(
                        $"Unexpected end of document while expecting a property name at position {cursor.Position}.");
                }

                if (cursor.Current != '"')
                {
                    throw new JsonParseException($"Expected a property name at position {cursor.Position}.");
                }

                string key = ParseString(ref cursor);
                cursor.SkipWhitespace();
                cursor.Expect(':');
                var value = ParseValue(ref cursor);

                // Last writer wins on a duplicate key, matching common JSON reader behaviour.
                members[key] = value;

                cursor.SkipWhitespace();
                if (cursor.TryConsume(','))
                {
                    continue;
                }

                if (cursor.TryConsume('}'))
                {
                    break;
                }

                throw new JsonParseException($"Expected ',' or '}}' at position {cursor.Position}.");
            }

            return JsonValue.NewObject(members);
        }

        private static JsonValue ParseArray(ref Cursor cursor)
        {
            cursor.Expect('[');
            var items = new List<JsonValue>();

            cursor.SkipWhitespace();
            if (cursor.TryConsume(']'))
            {
                return JsonValue.NewArray(items);
            }

            while (true)
            {
                var value = ParseValue(ref cursor);
                items.Add(value);

                cursor.SkipWhitespace();
                if (cursor.TryConsume(','))
                {
                    continue;
                }

                if (cursor.TryConsume(']'))
                {
                    break;
                }

                throw new JsonParseException($"Expected ',' or ']' at position {cursor.Position}.");
            }

            return JsonValue.NewArray(items);
        }

        private static string ParseString(ref Cursor cursor)
        {
            cursor.Expect('"');
            var sb = new StringBuilder();

            while (true)
            {
                if (cursor.AtEnd)
                {
                    throw new JsonParseException("Unterminated string literal.");
                }

                char c = cursor.Next();
                if (c == '"')
                {
                    break;
                }

                if (c == '\\')
                {
                    if (cursor.AtEnd)
                    {
                        throw new JsonParseException("Unterminated escape sequence.");
                    }

                    char escape = cursor.Next();
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append(ParseUnicodeEscape(ref cursor));
                            break;
                        default:
                            throw new JsonParseException($"Invalid escape '\\{escape}' at position {cursor.Position}.");
                    }
                }
                else if (c < 0x20)
                {
                    throw new JsonParseException($"Unescaped control character at position {cursor.Position}.");
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static char ParseUnicodeEscape(ref Cursor cursor)
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                if (cursor.AtEnd)
                {
                    throw new JsonParseException("Truncated \\u escape sequence.");
                }

                char c = cursor.Next();
                int digit = HexDigit(c);
                if (digit < 0)
                {
                    throw new JsonParseException($"Invalid hexadecimal digit '{c}' in \\u escape.");
                }

                value = (value << 4) | digit;
            }

            return (char)value;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static JsonValue ParseNumber(ref Cursor cursor)
        {
            int start = cursor.Position;

            if (cursor.Current == '-')
            {
                cursor.Advance();
            }

            // The integer part follows the JSON grammar `int = "0" / digit1-9 *DIGIT`: a single zero is
            // allowed (including the `0.x` and `0e0` forms), but a leading zero on a multi-digit integer
            // (`01`, `-007`) is rejected per RFC 8259 rather than silently accepted.
            ConsumeIntegerPart(ref cursor);

            if (!cursor.AtEnd && cursor.Current == '.')
            {
                cursor.Advance();
                ConsumeDigits(ref cursor, required: true);
            }

            if (!cursor.AtEnd && (cursor.Current == 'e' || cursor.Current == 'E'))
            {
                cursor.Advance();
                if (!cursor.AtEnd && (cursor.Current == '+' || cursor.Current == '-'))
                {
                    cursor.Advance();
                }

                ConsumeDigits(ref cursor, required: true);
            }

            string raw = cursor.Slice(start, cursor.Position);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                throw new JsonParseException($"Invalid number literal '{raw}'.");
            }

            return JsonValue.NewNumber(parsed, raw);
        }

        /// <summary>
        /// Consumes the integer part of a number, enforcing the JSON rule that a leading zero may only
        /// stand alone (a bare <c>0</c> or the <c>0</c> in <c>0.5</c>); <c>01</c> or <c>-007</c> is rejected.
        /// </summary>
        private static void ConsumeIntegerPart(ref Cursor cursor)
        {
            if (cursor.AtEnd || cursor.Current < '0' || cursor.Current > '9')
            {
                throw new JsonParseException($"Expected a digit at position {cursor.Position}.");
            }

            bool leadingZero = cursor.Current == '0';
            cursor.Advance();

            if (leadingZero && !cursor.AtEnd && cursor.Current >= '0' && cursor.Current <= '9')
            {
                throw new JsonParseException(
                    $"Invalid number with a leading zero at position {cursor.Position - 1}.");
            }

            // Consume the remaining integer digits (none when the integer part was a single 0 or digit).
            while (!cursor.AtEnd && cursor.Current >= '0' && cursor.Current <= '9')
            {
                cursor.Advance();
            }
        }

        private static void ConsumeDigits(ref Cursor cursor, bool required)
        {
            int consumed = 0;
            while (!cursor.AtEnd && cursor.Current >= '0' && cursor.Current <= '9')
            {
                cursor.Advance();
                consumed++;
            }

            if (required && consumed == 0)
            {
                throw new JsonParseException($"Expected a digit at position {cursor.Position}.");
            }
        }

        private static JsonValue ParseBoolean(ref Cursor cursor)
        {
            if (cursor.Current == 't')
            {
                ParseLiteral(ref cursor, "true");
                return JsonValue.NewBoolean(true);
            }

            ParseLiteral(ref cursor, "false");
            return JsonValue.NewBoolean(false);
        }

        private static void ParseLiteral(ref Cursor cursor, string literal)
        {
            foreach (char expected in literal)
            {
                if (cursor.AtEnd || cursor.Next() != expected)
                {
                    throw new JsonParseException($"Invalid literal; expected '{literal}'.");
                }
            }
        }

        /// <summary>
        /// A by-ref struct cursor over the source text. Mutating methods take <c>ref</c> so the single
        /// cursor instance threads through the recursive parse without boxing or per-call allocation.
        /// </summary>
        private struct Cursor
        {
            private readonly string _text;
            private int _position;

            public Cursor(string text)
            {
                _text = text;
                _position = 0;
            }

            public int Position => _position;

            public bool AtEnd => _position >= _text.Length;

            /// <summary>
            /// The character under the cursor. Throws a <see cref="JsonParseException"/> (never an
            /// <see cref="System.IndexOutOfRangeException"/>) when the cursor is at or past the end, so a
            /// truncated document surfaces as a clean diagnostic rather than crashing out of the generator.
            /// </summary>
            public char Current
            {
                get
                {
                    if (_position >= _text.Length)
                    {
                        throw new JsonParseException(
                            $"Unexpected end of document at position {_position}.");
                    }

                    return _text[_position];
                }
            }

            public void Advance() => _position++;

            /// <summary>
            /// Returns the character under the cursor and advances. Throws a
            /// <see cref="JsonParseException"/> rather than an <see cref="System.IndexOutOfRangeException"/>
            /// at end of document.
            /// </summary>
            public char Next()
            {
                if (_position >= _text.Length)
                {
                    throw new JsonParseException(
                        $"Unexpected end of document at position {_position}.");
                }

                return _text[_position++];
            }

            public string Slice(int start, int end) => _text.Substring(start, end - start);

            public void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _position++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public void Expect(char expected)
            {
                if (AtEnd || _text[_position] != expected)
                {
                    throw new JsonParseException($"Expected '{expected}' at position {_position}.");
                }

                _position++;
            }

            public bool TryConsume(char expected)
            {
                if (!AtEnd && _text[_position] == expected)
                {
                    _position++;
                    return true;
                }

                return false;
            }
        }
    }
}
