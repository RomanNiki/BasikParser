using System.Collections;
using System.Globalization;
using System.Text;

namespace BasicLexer;

public class Lexer : IEnumerable<Token>, IEnumerator<Token>
{
    private readonly LexerSettings _settings;
    private LexerBehavior _behavior;
    private TextReader _reader;
    private string _text;
    private int _position;
    private int _start;
    private int _textLen;
    private int _textPos;
    private int _textBeg;
    private int _bufBeg;
    private int _maxSymLen;
    private int _lineBegin;
    private int _lineNumber;
    private int _endLineBegin;
    private int _endLineNumber;
    private StringBuilder _buffer;
    private StringBuilder _tokenBuffer;
    private Token _next;

    private Lexer(string text, TextReader reader, LexerBehavior behavior, LexerSettings settings)
    {
        settings = settings == null ? LexerSettings.Default : settings.Clone();

        _text = text;
        _reader = reader;
        _behavior = behavior;
        _settings = settings;

        if (settings.Symbols != null)
        {
            foreach (var entry in settings.Symbols)
            {
                var len = entry.Key.Length;
                if (len > _maxSymLen)
                {
                    _maxSymLen = len;
                }
            }
        }

        Reset();
    }

    public Lexer(string text, LexerBehavior behavior, LexerSettings settings)
        : this(text, null, behavior, settings)
    {
    }

    private const int BufferCapacity = 8192;

    private const char EndOfTextChar = unchecked((char)-1);

    public Token Current { get; private set; }

    public bool IsEmpty => _text == null;

    public void Reset()
    {
        var readerPos = _position - _textPos;
        Current = new Token(TokenType.Start, null, null, CommonLexem.Start, 0, 0, 0, 0, 0, 0);
        _next = null;
        _textPos = 0;
        _position = 0;
        _textBeg = 0;
        _tokenBuffer = null;
        _buffer = null;
        _bufBeg = -1;

        if (_reader != null)
        {
            if (_text != null && readerPos > 0)
            {
                if (_reader is StreamReader { BaseStream.CanSeek: true } streamReader)
                {
                    streamReader.BaseStream.Seek(0, SeekOrigin.Begin);
                    _text = null;
                }
            }

            if (_text != null) return;
            _textLen = 0;
            ReadCharBuffer();
        }
        else
        {
            _textLen = _text?.Length ?? 0;
        }
    }

    public Token GetNextToken(LexerBehavior behavior)
    {
        var saveBehavior = _behavior;
        _behavior = behavior;
        try
        {
            return GetNextToken();
        }
        finally
        {
            _behavior = saveBehavior;
        }
    }

    private Token GetNextToken()
    {
        if (_next != null)
        {
            Current = _next;
            _next = null;
        }
        else
        {
            Current = GetToken();
        }

        return Current;
    }

    public Token PeekNextToken(LexerBehavior behavior)
    {
        var saveBehavior = _behavior;
        _behavior = behavior;
        try
        {
            return PeekNextToken();
        }
        finally
        {
            _behavior = saveBehavior;
        }
    }

    private Token PeekNextToken()
    {
        return _next ??= GetToken();
    }

    #region Private Implementation

    private Token GetToken()
    {
        if (_text == null)
        {
            return new Token(TokenType.End, "", "", CommonLexem.End, 0, 0, 0, 0, 0, 0);
        }

        _lineBegin = _endLineBegin;
        _lineNumber = _endLineNumber;
        _start = _position;
        _textBeg = _textPos;
        _bufBeg = -1;
        _tokenBuffer = null;
        _buffer = null;

        var currentChar = PeekChar();
        bool skip;
        do
        {
            skip = false;
            switch (currentChar)
            {
                // end
                case EndOfTextChar when EndOfText():
                    return GetEndToken();
                // separator
                case <= ' ':
                {
                    var skipWhiteSpaces = (_behavior & LexerBehavior.SkipWhiteSpaces) != 0;
                    do
                    {
                        ReadNext();
                        if (skipWhiteSpaces)
                        {
                            _textBeg = _textPos;
                        }

                        if (EndOfLine(currentChar))
                        {
                            if (skipWhiteSpaces)
                            {
                                _textBeg = _textPos;
                            }
                            else if ((_settings.Options & LexerOptions.EndOfLineAsToken) != 0)
                            {
                                return new Token(TokenType.EndOfLine, "", GetTokenText(), 0, _start, _position,
                                    _lineBegin, _lineNumber, _endLineBegin, _endLineNumber);
                            }
                        }

                        currentChar = PeekChar();
                        if (currentChar == EndOfTextChar && EndOfText())
                        {
                            break;
                        }
                    } while (currentChar <= ' ');

                    if (!skipWhiteSpaces)
                    {
                        return new Token(TokenType.WhiteSpace, "", GetTokenText(), 0, _start, _position, _lineBegin,
                            _lineNumber, _endLineBegin, _endLineNumber);
                    }

                    _textBeg = _textPos;
                    skip = true;
                    _start = _position;
                    break;
                }
            }

            // inline comment
            var inlineComments = _settings.InlineComments;
            if (inlineComments != null)
            {
                if (inlineComments.Any(NextSymbolIs))
                {
                    var skipComments = ((_behavior & LexerBehavior.SkipComments) != 0);
                    skip = true;
                    if (skipComments)
                    {
                        _textBeg = _textPos;
                    }

                    currentChar = PeekChar();
                    while (true)
                    {
                        if (currentChar is '\r' or '\n')
                        {
                            break;
                        }

                        currentChar = NextChar();
                        if (currentChar == EndOfTextChar && EndOfText())
                        {
                            break;
                        }

                        if (skipComments)
                        {
                            _textBeg = _textPos;
                        }
                    }

                    if (skipComments)
                    {
                        _start = _position;
                    }
                    else
                    {
                        return new Token(TokenType.Comment, "", GetTokenText(), 0, _start, _position, _lineBegin,
                            _lineNumber, _lineBegin, _lineNumber);
                    }
                }
            }

            // comment
            if (!string.IsNullOrEmpty(_settings.CommentBegin) && NextSymbolIs(_settings.CommentBegin))
            {
                var skipComments = ((_behavior & LexerBehavior.SkipComments) != 0);
                skip = true;
                if (skipComments)
                {
                    _textBeg = _textPos;
                }

                while (true)
                {
                    if (NextSymbolIs(_settings.CommentEnd))
                    {
                        currentChar = PeekChar();
                        if (skipComments)
                        {
                            _textBeg = _textPos;
                        }

                        break;
                    }

                    currentChar = NextChar();
                    if (currentChar == EndOfTextChar && EndOfText())
                    {
                        break;
                    }
                    else
                    {
                        EndOfLine(currentChar);
                    }

                    if (skipComments)
                    {
                        _textBeg = _textPos;
                    }
                }

                if (skipComments)
                {
                    _start = _position;
                }
                else
                {
                    return new Token(TokenType.Comment, "", GetTokenText(), 0, _start, _position, _lineBegin,
                        _lineNumber, _endLineBegin, _endLineNumber);
                }
            }

            _lineNumber = _endLineNumber;
            _lineBegin = _endLineBegin;
        } while (skip);

        // quoted string
        var stringQuotes = _settings.StringQuotes;
        if (stringQuotes != null)
        {
            for (var i = 0; i < stringQuotes.Length; i++)
            {
                var stringQuoteChar = stringQuotes[i];
                if (currentChar == stringQuoteChar ||
                    i == 0 && currentChar == _settings.StringPrefix && PeekChar(1) == stringQuoteChar)
                {
                    return GetQuotedStringToken(currentChar != stringQuoteChar, stringQuoteChar);
                }
            }
        }

        // quoted identifier
        var isIdentQuote = currentChar == _settings.IdentQuote;
        var quote = isIdentQuote || currentChar == _settings.IdentQuoteBegin;
        char nextChar;
        if (quote || currentChar == _settings.IdentPrefix && (isIdentQuote =
                (nextChar = PeekChar(1)) == _settings.IdentQuote || nextChar == _settings.IdentQuoteBegin))
        {
            return GetQuotedIdentifierToken(!quote, isIdentQuote);
        }

        // prefix identifier
        if (currentChar == _settings.IdentPrefix)
        {
            return GetPrefixedIdentifierToken();
        }

        // number
        if (currentChar is >= '0' and <= '9')
        {
            return GetNumberToken(currentChar);
        }

        // keyword / identifier
        if (char.IsLetter(currentChar) || currentChar == '_' || IsIdentChar(currentChar))
        {
            return GetKeywordOrIdentifierToken(currentChar);
        }

        // predefined symbol
        if (_settings.Symbols != null)
        {
            var symbol = PeekSubstring(_maxSymLen);
            for (var i = symbol.Length; i > 0; i--, symbol = symbol[..i])
            {
                if (_settings.Symbols.TryGetValue(symbol, out var symbolId) == false) continue;
                Skip(i);
                var symbolText = (_behavior & LexerBehavior.PersistTokenText) != 0 ? symbol : null;
                return new Token(TokenType.Symbol, symbol, symbolText, symbolId, _start, _position, _lineBegin,
                    _lineNumber, _lineBegin, _lineNumber);
            }
        }

        // just a char
        currentChar = NextChar();
        var charText = (_behavior & LexerBehavior.PersistTokenText) != 0 ? currentChar.ToString() : null;
        return new Token(TokenType.Char, currentChar, charText, 0, _start, _position, _lineBegin, _lineNumber,
            _lineBegin, _lineNumber);
    }

    private Token GetEndToken()
    {
        _reader?.Close();

        return new Token(TokenType.End, "", "", CommonLexem.End, _start, _start, _lineBegin, _lineNumber, _lineBegin,
            _lineNumber);
    }

    private Token GetQuotedIdentifierToken(bool prefix, bool isIdentQuote)
    {
        if (prefix)
        {
            ReadNext();
        }

        char quoteEnd;
        bool doubleQuote;
        if (isIdentQuote)
        {
            quoteEnd = _settings.IdentQuote;
            doubleQuote = (_settings.Options & LexerOptions.IdentDoubleQuote) != 0;
        }
        else
        {
            quoteEnd = _settings.IdentQuoteEnd;
            doubleQuote = false;
        }

        ReadNext();
        _bufBeg = _textPos;

        while (true)
        {
            var currentChar = NextChar();
            BufferAdd(currentChar);

            if (currentChar == quoteEnd)
            {
                if (doubleQuote && PeekChar() == quoteEnd)
                {
                    EnsureBuffer(1);
                    currentChar = NextChar();
                    BufferAdd(currentChar);
                }
                else
                {
                    break;
                }
            }

            if (currentChar == EndOfTextChar && EndOfText())
            {
                break;
            }

            EndOfLine(currentChar);
        }

        string val = GetBufferValue(-1);
        return new Token(TokenType.Identifier, val, GetTokenText(), 0, _start, _position, _lineBegin, _lineNumber,
            _endLineBegin, _endLineNumber);
    }

    private Token GetQuotedStringToken(bool prefix, char stringQuoteChar)
    {
        char escapeChar;
        bool escaping;
        bool doubleQuote;
        if (prefix)
        {
            escapeChar = '\0';
            escaping = false;
            doubleQuote = true;
            ReadNext();
        }
        else
        {
            escapeChar = _settings.StringEscapeChar;
            escaping = (_settings.Options & LexerOptions.StringEscaping) != 0;
            doubleQuote = (_settings.Options & LexerOptions.StringDoubleQuote) != 0;
        }

        ReadNext();
        _bufBeg = _textPos;

        while (true)
        {
            char currentChar = NextChar();
            BufferAdd(currentChar);

            if (currentChar == escapeChar && escaping)
            {
                EnsureBuffer(1);
                currentChar = NextChar();
                BufferAdd(currentChar);
            }
            else if (currentChar == stringQuoteChar)
            {
                if (doubleQuote && PeekChar() == stringQuoteChar)
                {
                    EnsureBuffer(1);
                    currentChar = NextChar();
                    BufferAdd(currentChar);
                }
                else
                {
                    break;
                }
            }
            else if (currentChar == EndOfTextChar && EndOfText())
            {
                break;
            }
            else
            {
                EndOfLine(currentChar);
            }
        }

        var val = GetBufferValue(-1);
        return new Token(TokenType.QuotedString, val, GetTokenText(), 0, _start, _position, _lineBegin, _lineNumber,
            _endLineBegin, _endLineNumber);
    }

    private Token GetKeywordOrIdentifierToken(char currentChar)
    {
        _bufBeg = _textPos;
        do
        {
            ReadNext();
            BufferAdd(currentChar);
            currentChar = PeekChar();
        } while (char.IsLetterOrDigit(currentChar) || currentChar == '_' || IsIdentChar(currentChar));

        var val = GetBufferValue(0);

        var id = 0;
        var tokenType = TokenType.Identifier;
        if ((_settings.Options & LexerOptions.IdentToUpper) != 0)
        {
            val = val.ToUpper(_settings.CultureInfo);
            if (_settings.Keywords != null && _settings.Keywords.TryGetValue(val, out id))
            {
                tokenType = TokenType.Keyword;
            }
        }
        else
        {
            if (_settings.Keywords != null && _settings.Keywords.TryGetValue(val, out id))
            {
                tokenType = TokenType.Keyword;
            }

            if ((_settings.Options & LexerOptions.IdentToLower) != 0)
            {
                val = val.ToLower();
            }
        }

        return new Token(tokenType, val, GetTokenText(), id, _start, _position, _lineBegin, _lineNumber,
            _lineBegin, _lineNumber);
    }

    private Token GetNumberToken(char currentChar)
    {
        _bufBeg = _textPos;
        do
        {
            ReadNext();
            BufferAdd(currentChar);
            currentChar = PeekChar();
        } while (currentChar is >= '0' and <= '9');

        var decimalSeparator = _settings.DecimalSeparator;
        if (SymbolIs(decimalSeparator))
        {
            var ln = decimalSeparator.Length;
            var ch = PeekChar(ln);
            if (ch is >= '0' and <= '9')
            {
                Skip(ln);
                BufferAdd(decimalSeparator);
                currentChar = ch;
                do
                {
                    ReadNext();
                    BufferAdd(currentChar);
                    currentChar = PeekChar();
                } while (currentChar is >= '0' and <= '9');
            }
        }

        if (char.IsLetter(currentChar))
        {
            do
            {
                ReadNext();
                BufferAdd(currentChar);
                currentChar = PeekChar();
            } while (currentChar is >= '0' and <= '9' or '-' or '+' || char.IsLetter(currentChar));

            var val = GetBufferValue(0);
            return new Token(TokenType.Number, val, GetTokenText(), 0, _start, _position, _lineBegin, _lineNumber,
                _lineBegin, _lineNumber);
        }
        else
        {
            var val = GetBufferValue(0);
            decimal decimalVal;
            long intVal;
            if (long.TryParse(val, out intVal))
            {
                return new Token(TokenType.Integer, intVal, GetTokenText(), 0, _start, _position, _lineBegin,
                    _lineNumber, _lineBegin, _lineNumber);
            }

            return decimal.TryParse(val, out decimalVal)
                ? new Token(TokenType.Decimal, decimalVal, GetTokenText(), 0, _start, _position, _lineBegin,
                    _lineNumber, _lineBegin, _lineNumber)
                : new Token(TokenType.Number, val, GetTokenText(), 0, _start, _position, _lineBegin, _lineNumber,
                    _lineBegin, _lineNumber);
        }
    }

    private Token GetPrefixedIdentifierToken()
    {
        ReadNext();
        _bufBeg = _textPos;

        var currentChar = PeekChar();
        while (char.IsLetterOrDigit(currentChar) || currentChar == '_' || IsIdentChar(currentChar))
        {
            ReadNext();
            BufferAdd(currentChar);
            currentChar = PeekChar();
        }

        var val = GetBufferValue(0);
        if ((_settings.Options & LexerOptions.IdentToUpper) != 0)
        {
            val = val.ToUpper(_settings.CultureInfo);
        }
        else if ((_settings.Options & LexerOptions.IdentToLower) != 0)
        {
            val = val.ToLower(_settings.CultureInfo);
        }

        return new Token(TokenType.Identifier, val, GetTokenText(), 0, _start, _position, _lineBegin, _lineNumber,
            _lineBegin, _lineNumber);
    }

    private bool IsIdentChar(char currentChar)
    {
        var identChars = _settings.IdentChars;
        if (identChars == null) return false;
        var len = identChars.Length;
        for (var i = 0; i < len; i++)
        {
            var ch = identChars[i];
            if (currentChar == ch)
            {
                return true;
            }
        }

        return false;
    }

    private char PeekChar()
    {
        if (_textPos < _textLen)
        {
            return _text[_textPos];
        }

        if (_textLen != BufferCapacity) return EndOfTextChar;
        ReadCharBuffer();
        return _textPos < _textLen ? _text[_textPos] : EndOfTextChar;
    }

    private char PeekChar(int ofs)
    {
        var i = _textPos + ofs;
        if (i < _textLen)
        {
            return _text[i];
        }

        if (_textLen != BufferCapacity) return EndOfTextChar;
        ReadCharBuffer();
        ofs += _textPos;
        return ofs < _textLen ? _text[ofs] : EndOfTextChar;
    }

    private string PeekSubstring(int count)
    {
        if (_textPos + count <= _textLen)
        {
            return _text.Substring(_textPos, count);
        }

        if (_textLen == BufferCapacity)
        {
            ReadCharBuffer();
        }

        var i = _textLen - _textPos;
        return _text.Substring(_textPos, count <= i ? count : i);
    }

    private char NextChar()
    {
        if (_textPos < _textLen)
        {
            _position++;
            return _text[_textPos++];
        }

        if (_textLen != BufferCapacity) return EndOfTextChar;
        ReadCharBuffer();
        if (_textPos >= _textLen) return EndOfTextChar;
        _position++;
        return _text[_textPos++];
    }

    private void ReadNext()
    {
        if (_textPos < _textLen)
        {
            _position++;
            _textPos++;
        }
        else
        {
            if (_textLen != BufferCapacity) return;
            ReadCharBuffer();
            _position++;
            _textPos++;
        }
    }

    private bool NextSymbolIs(string s)
    {
        var ln = s.Length;
        if (_textLen - _textPos < ln && _textLen == BufferCapacity)
        {
            ReadCharBuffer();
        }

        if (_textLen - _textPos < ln || _text[_textPos] != s[0])
        {
            return false;
        }

        if (_settings.CompareInfo.Compare(_text, _textPos, ln, s, 0, ln, CompareOptions.None) != 0) return false;
        _position += ln;
        _textPos += ln;
        return true;
    }

    private bool SymbolIs(string s)
    {
        var ln = s.Length;
        if (_textLen - _textPos < ln && _textLen == BufferCapacity)
        {
            ReadCharBuffer();
        }

        if (_textLen - _textPos < ln || _text[_textPos] != s[0])
        {
            return false;
        }

        return (_settings.CompareInfo.Compare(_text, _textPos, ln, s, 0, ln, CompareOptions.None) == 0);
    }

    private void Skip(int ofs)
    {
        if (_textLen - _textPos < ofs && _textLen == BufferCapacity)
        {
            ReadCharBuffer();
        }

        var i = Math.Min(_textLen - _textPos, ofs);
        _position += i;
        _textPos += i;
    }

    private bool EndOfLine(char currentChar)
    {
        switch (currentChar)
        {
            case '\r':
            {
                _endLineNumber++;
                _endLineBegin = _position;
                currentChar = PeekChar();
                if (currentChar != '\n') return true;
                ReadNext();
                BufferAdd(currentChar);
                _endLineBegin = _position;

                return true;
            }
            case '\n':
                _endLineNumber++;
                _endLineBegin = _position;

                return true;
            default:
                return false;
        }
    }

    private bool EndOfText()
    {
        if (_textPos < _textLen)
        {
            return false;
        }

        if (_textLen != BufferCapacity) return true;
        ReadCharBuffer();
        return _textPos >= _textLen;

    }

    private void BufferAdd(char currentChar)
    {
        if (_buffer != null)
        {
            _buffer.Append(currentChar);
        }
        else if (_bufBeg >= 0 && _textPos >= _textLen)
        {
            _buffer = new StringBuilder(_text, _bufBeg, _textPos - _bufBeg, BufferCapacity);
        }
    }

    private void BufferAdd(string str)
    {
        if (_buffer != null)
        {
            _buffer.Append(str);
        }
        else if (_bufBeg >= 0 && _textPos >= _textLen)
        {
            _buffer = new StringBuilder(_text, _bufBeg, _textPos - _bufBeg, BufferCapacity);
        }
    }

    private void EnsureBuffer(int ofs)
    {
        if (_buffer == null)
        {
            _buffer = new StringBuilder(_text, _bufBeg, _textPos - _bufBeg - ofs, BufferCapacity);
        }
        else
        {
            _buffer.Remove(_buffer.Length - ofs, ofs);
        }
    }

    private string GetBufferValue(int ofs)
    {
        if (_buffer != null)
        {
            return _buffer.ToString(0, _buffer.Length + ofs);
        }
        else
        {
            return _text.Substring(_bufBeg, _textPos - _bufBeg + ofs);
        }
    }

    private void ReadCharBuffer()
    {
        if (_reader == null)
        {
            return;
        }

        if (_tokenBuffer != null)
        {
            _tokenBuffer.Append(_text, 0, _textPos);
        }
        else if (_textBeg < _textPos && (_behavior & LexerBehavior.PersistTokenText) != 0)
        {
            _tokenBuffer = new StringBuilder(_text, _textBeg, _textPos - _textBeg, BufferCapacity);
        }
        else
        {
            _textBeg = 0;
        }

        var charBuffer = new char[BufferCapacity];
        if (_textPos < _textLen)
        {
            if (_textPos == 0)
            {
                throw new ArgumentException("'BufferCapacity' too small.");
            }

            _textLen -= _textPos;
            _text.CopyTo(_textPos, charBuffer, 0, _textLen);
        }
        else
        {
            _textLen = 0;
        }

        _textLen += _reader.Read(charBuffer, _textLen, BufferCapacity - _textLen);
        _text = new string(charBuffer, 0, _textLen);
        _textPos = 0;
    }

    private string GetTokenText()
    {
        if (_tokenBuffer == null)
            return (_behavior & LexerBehavior.PersistTokenText) == 0
                ? null
                : _text.Substring(_textBeg, _textPos - _textBeg);
        _tokenBuffer.Append(_text, 0, _textPos);
        return _tokenBuffer.ToString(0, _tokenBuffer.Length);

    }

    #endregion

    #region IEnumerable<Token> Members

    IEnumerator<Token> IEnumerable<Token>.GetEnumerator()
    {
        return this;
    }

    #endregion

    #region IEnumerable Members

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this;
    }

    #endregion

    #region IEnumerator Members

    object IEnumerator.Current
    {
        get { return Current; }
    }

    bool IEnumerator.MoveNext()
    {
        return GetNextToken().Type != TokenType.End;
    }

    #endregion

    #region IDisposable Members

    public void Dispose() => _reader?.Dispose();

    #endregion
}

public enum TokenType
{
    Char,
    Symbol,
    Number,
    Decimal,
    Integer,
    Identifier,
    Keyword,
    QuotedString,
    WhiteSpace,
    EndOfLine,
    Comment,
    Start,
    End,
}

[Flags]
public enum LexerBehavior
{
    SkipWhiteSpaces = 1,
    SkipComments = 2,
    PersistTokenText = 4,
    Default = PersistTokenText
}

[Flags]
public enum LexerOptions
{
    IdentIgnoreCase = 1,
    IdentToLower = 2,
    IdentToUpper = 4,
    IdentDoubleQuote = 8,
    StringEscaping = 16,
    StringDoubleQuote = 32,
    EndOfLineAsToken = 64
}

public sealed class Token
{
    public readonly TokenType Type;
    public readonly object Value;
    public readonly string Text;
    public readonly int Id;
    public readonly int StartPosition;
    public readonly int EndPosition;
    public readonly int LineBegin;
    public readonly int LineNumber;
    public readonly int EndLineBegin;
    public readonly int EndLineNumber;

    public Token(TokenType type, object value, string text, int id, int startPosition, int endPosition, int lineBegin,
        int lineNumber, int endLineBegin, int endLineNumber)
    {
        Type = type;
        Value = value;
        Text = text;
        Id = id;
        StartPosition = startPosition;
        EndPosition = endPosition;
        LineBegin = lineBegin;
        LineNumber = lineNumber;
        EndLineBegin = endLineBegin;
        EndLineNumber = endLineNumber;
    }

    public int LinePosition => StartPosition - LineBegin;

    public int EndLinePosition => EndPosition - EndLineBegin;

    public override string ToString()
    {
        return $"({Type}, {Id}, '{Text}')";
    }
}

public sealed class LexerSettings : ICloneable
{
    public LexerOptions Options { get; set; }
    public IDictionary<string, int> Symbols { get; set; }
    public IDictionary<string, int> Keywords { get; set; }
    public CultureInfo CultureInfo { get; set; }
    public CompareInfo CompareInfo { get; set; }
    public char[] StringQuotes { get; set; }
    public char StringEscapeChar { get; set; }
    public char StringPrefix { get; set; }
    public char IdentQuote { get; set; }
    public char IdentQuoteBegin { get; set; }
    public char IdentQuoteEnd { get; set; }
    public char IdentPrefix { get; set; }
    public char[] IdentChars { get; set; }
    public string[] InlineComments { get; set; }
    public string CommentBegin { get; set; }
    public string CommentEnd { get; set; }
    public string DecimalSeparator { get; set; }

    public static LexerSettings Default
    {
        get
        {
            var settings = new LexerSettings
            {
                CultureInfo = CultureInfo.InvariantCulture,
                CompareInfo = CultureInfo.InvariantCulture.CompareInfo,
                DecimalSeparator = ".",
                Options = LexerOptions.IdentIgnoreCase | LexerOptions.StringDoubleQuote,
                StringQuotes = new char[] { '\"', '\'' },
                InlineComments = new string[] { "//" },
                CommentBegin = "/*",
                CommentEnd = "*/",
                StringEscapeChar = '\\',
                StringPrefix = '@',
                IdentQuote = '\0',
                IdentQuoteBegin = '\0',
                IdentQuoteEnd = '\0'
            };

            return settings;
        }
    }

    #region ICloneable Members

    object ICloneable.Clone()
    {
        return Clone();
    }

    public LexerSettings Clone()
    {
        var settings = (LexerSettings)MemberwiseClone();

        settings.CultureInfo ??= CultureInfo.InvariantCulture;

        settings.CompareInfo ??= settings.CultureInfo.CompareInfo;

        if (string.IsNullOrEmpty(settings.DecimalSeparator))
        {
            settings.DecimalSeparator = settings.CultureInfo.NumberFormat.NumberDecimalSeparator;
        }

        settings.Symbols = settings.Symbols is { Count: > 0 } ? new Dictionary<string, int>(settings.Symbols) : null;

        if (settings.Keywords is { Count: > 0 })
        {
            var ignoreCase = settings.Options.HasFlag(LexerOptions.IdentIgnoreCase);
            settings.Keywords = new Dictionary<string, int>(settings.Keywords,
                StringComparer.Create(settings.CultureInfo, ignoreCase));
        }
        else
        {
            settings.Keywords = null;
        }

        if (settings.StringQuotes != null)
        {
            settings.StringQuotes = (char[])settings.StringQuotes.Clone();
        }

        if (settings.IdentChars != null)
        {
            settings.IdentChars = (char[])settings.IdentChars.Clone();
        }

        var inlineComments = settings.InlineComments;
        if (inlineComments != null)
        {
            var length = inlineComments.Length;
            var count = 0;
            for (var i = 0; i < length; i++)
            {
                var inlineComment = inlineComments[i];
                if (inlineComment == null)
                {
                    continue;
                }

                if (i != count)
                {
                    inlineComments[count] = inlineComment;
                }

                count++;
            }

            if (count == 0)
            {
                settings.InlineComments = null;
            }
            else
            {
                var arr = new string[count];
                Array.Copy(inlineComments, 0, arr, 0, count);
            }
        }

        if (!string.IsNullOrEmpty(settings.CommentBegin) && string.IsNullOrEmpty(settings.CommentEnd))
        {
            settings.CommentEnd = settings.CommentBegin;
        }

        return settings;
    }

    #endregion
}

internal static class CommonLexem
{
    public const int Start = 1;
    public const int End = 2;
}