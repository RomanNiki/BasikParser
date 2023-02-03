namespace BasicLexer;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _currentIndex;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public bool Validate()
    {
        return FunctionDef();
    }

    private bool FunctionDef()
    {
        var t = Next();

        if (t.Type != TokenType.Keyword || t.Id != (int) KeywordType.KwSub)
        {
            return false;
        }

        if (!Identifier())
        {
            return false;
        }

        t = Next();
        if (t.Type != TokenType.Symbol || t.Id != (int) PunctuationType.Lp)
        {
            return false;
        }

        t = Next();
        if (t.Type != TokenType.Symbol || t.Id != (int) PunctuationType.Rp)
        {
            return false;
        }

        t = Next();
        if ( t.Type != TokenType.EndOfLine)
        {
            return false;
        }
        
        if (!IsStatement())
        {
            return false;
        }
        SkipClearRows(ref t);
       
        if (t.Type != TokenType.Keyword || t.Id != (int) KeywordType.KwEnd)
        {
            return false;
        }
        t = Next();
        if (t.Type !=  TokenType.WhiteSpace)
        {
            return false;
        }
        t = Next();
        return t.Type == TokenType.Keyword && t.Id == (int) KeywordType.KwSub;
    }

    private void SkipClearRows(ref Token t)
    {
        while (TokenType.EndOfLine == t.Type)
        {
            t = Next();
        } 
    }
    
    private bool IsStatement()
    {
        var t = Next();
        SkipClearRows(ref t);

        if (t.Type != TokenType.WhiteSpace)
        {
            Next();
        }
        
        t = Next();
        if (t.Type == TokenType.Identifier)
        {
            if (!IsFunctionOrMacrosCall() && !IsExpression())
                return false;
        }

        t = Next();
        return t.Type == TokenType.EndOfLine;
    }

    private bool IsExpression()
    {
        return IsValue() && IsOperator() && IsValue() && IsOperator();
    }

    private bool IsValue()
    {
        var t = Next();

        if (t.Type == TokenType.End) return false;

        return t.Type == TokenType.QuotedString ||
               t.Type == TokenType.Identifier ||
               t.Type == TokenType.Decimal ||
               t.Type == TokenType.Number ||
               IsFunctionOrMacrosCall() || 
               IsExpression();
    }

    private bool IsOperator()
    {
        var t = Next();
        return t.Type == TokenType.Symbol;
    }

    private bool IsFunctionOrMacrosCall()
    {
        var t = Next();
        if (t.Type == TokenType.Symbol && t.Id == (int) PunctuationType.Not)
        {
            t = Next();
        }

        if (t.Type != TokenType.Symbol || t.Id != (int) PunctuationType.Lp)
        {
            return false;
        }

        t = Peek();
        while (FuncCallArgumentShouldNext(t))
        {
            if (!IsValue())
            {
                return false;
            }
            
            t = Peek();
        }

        t = Next();
        return t.Type == TokenType.Symbol && t.Id == (int) PunctuationType.Rp;
    }

    private bool FuncCallArgumentShouldNext(Token t)
    {
        return t.Type != TokenType.Symbol && t.Id != (int) PunctuationType.Rp ||
               t.Type != TokenType.Symbol && t.Id != (int) PunctuationType.Comma;
    }

    private bool Identifier()
    {
        var t = Next();

        if (t.Type != TokenType.WhiteSpace)
        {
            return false;
        }
        t = Next();
        return t.Type == TokenType.Identifier;
    }

    private Token Next()
    {
        return _currentIndex >= _tokens.Count ? new Token(TokenType.End, null, string.Empty, 0, 0, 0, 0, 0, 0, 0) : _tokens[_currentIndex++];
    }

    private Token Peek() => _tokens[_currentIndex];
}