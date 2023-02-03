namespace BasicLexer;

public class Program
{
    private const string Code = """
Sub Main()
    Console.WriteLine("This is comments.vb")
End Sub
""";

    private const string NotValidCode = """
Sub Main
    Console
Sub
""";

    private static void Main(string[] args)
    {
        var keywords = new Dictionary<string, int>
        {
            ["False"] = (int)KeywordType.KwFalse,
            ["Sub"] = (int)KeywordType.KwSub,
            ["For"] = (int)KeywordType.KwFor,
            ["If"] = (int)KeywordType.KwIf,
            ["Else"] = (int)KeywordType.KwElse,
            ["Dim"] = (int)KeywordType.KwDim,
            ["While"] = (int)KeywordType.KwWhile,
            ["As"] = (int)KeywordType.KwAs,
            ["End"] = (int)KeywordType.KwEnd,
            ["Module"] = (int)KeywordType.KwModule,
            ["Next"] = (int)KeywordType.Next,
            ["Step"] = (int)KeywordType.KwStep,
            ["To"] = (int)KeywordType.KwTo,
        };

        var symbols = new Dictionary<string, int>
        {
            ["+"] = (int)PunctuationType.Plus,
            ["-"] = (int)PunctuationType.Minus,
            ["*"] = (int)PunctuationType.Star,
            ["/"] = (int)PunctuationType.Slash,
            ["%"] = (int)PunctuationType.Percent,
            ["^"] = (int)PunctuationType.Caret,
            ["!"] = (int)PunctuationType.Not,
            ["&"] = (int)PunctuationType.And,
            ["|"] = (int)PunctuationType.Or,
            ["&&"] = (int)PunctuationType.AndAnd,
            ["||"] = (int)PunctuationType.OrOr,
            ["<<"] = (int)PunctuationType.Shl,
            [">>"] = (int)PunctuationType.Shr,
            ["+="] = (int)PunctuationType.PlusEq,
            ["-="] = (int)PunctuationType.MinusEq,
            ["*="] = (int)PunctuationType.StarEq,
            ["/="] = (int)PunctuationType.SlashEq,
            ["%="] = (int)PunctuationType.PercentEq,
            ["^="] = (int)PunctuationType.CaretEq,
            ["&="] = (int)PunctuationType.AndEq,
            ["|="] = (int)PunctuationType.OrEq,
            ["<<="] = (int)PunctuationType.ShlEq,
            [">>="] = (int)PunctuationType.ShrEq,
            ["="] = (int)PunctuationType.Eq,
            ["=="] = (int)PunctuationType.EqEq,
            ["!="] = (int)PunctuationType.Ne,
            [">"] = (int)PunctuationType.Gt,
            ["<"] = (int)PunctuationType.Lt,
            [">="] = (int)PunctuationType.Ge,
            ["<="] = (int)PunctuationType.Le,
            ["@"] = (int)PunctuationType.At,
            ["_"] = (int)PunctuationType.Underscore,
            ["."] = (int)PunctuationType.Dot,
            [".."] = (int)PunctuationType.DotDot,
            ["..."] = (int)PunctuationType.DotDotDot,
            ["..="] = (int)PunctuationType.DotDotEq,
            [","] = (int)PunctuationType.Comma,
            [":"] = (int)PunctuationType.Colon,
            ["::"] = (int)PunctuationType.PathSep,
            ["->"] = (int)PunctuationType.RArrow,
            ["=>"] = (int)PunctuationType.FatArrow,
            ["#"] = (int)PunctuationType.Pound,
            ["$"] = (int)PunctuationType.Dollar,
            ["?"] = (int)PunctuationType.Question,
            ["~"] = (int)PunctuationType.Tilde,
            ["("] = (int)PunctuationType.Lp,
            [")"] = (int)PunctuationType.Rp,
            ["["] = (int)PunctuationType.Lsb,
            ["]"] = (int)PunctuationType.Rsb,
            ["\n\r"] = (int)PunctuationType.EndOfString,
            ["\r\n"] = (int)PunctuationType.EndOfString,
            [" "] = (int)PunctuationType.Space
        };

        Console.WriteLine("Code: " + Code);

        var lexerSettings = new LexerSettings
        {
            Keywords = keywords,
            Symbols = symbols,
            DecimalSeparator = ".",
            InlineComments = new[] { "//" },
            CommentBegin = "/*",
            CommentEnd = "*/",
            StringQuotes = new[] { '\"' },
            StringEscapeChar = '\\',
            Options = LexerOptions.IdentToLower | LexerOptions.EndOfLineAsToken
        };
        var lexer = new Lexer(Code, LexerBehavior.PersistTokenText | LexerBehavior.SkipComments, lexerSettings);
        var tokens = lexer.ToList();
        Console.WriteLine("Tokens: " + string.Join(", ", tokens));

        var r = new Parser(tokens).Validate();
        Console.WriteLine("IsValid: " + r);

        Console.WriteLine("NotValidCode: " + NotValidCode);
        lexer = new Lexer(NotValidCode, LexerBehavior.PersistTokenText | LexerBehavior.SkipComments, lexerSettings);
        tokens = lexer.ToList();
        Console.WriteLine("Tokens: " + string.Join(", ", tokens));
        r = new Parser(tokens).Validate();
        Console.WriteLine("IsValid: " + r);
    }
}

public enum PunctuationType
{
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Caret,
    Not,
    And,
    Or,
    AndAnd,
    OrOr,
    Shl,
    Shr,
    PlusEq,
    MinusEq,
    StarEq,
    SlashEq,
    PercentEq,
    CaretEq,
    AndEq,
    OrEq,
    ShlEq,
    ShrEq,
    Eq,
    EqEq,
    Ne,
    Gt,
    Lt,
    Ge,
    Le,
    At,
    Underscore,
    Dot,
    DotDot,
    DotDotDot,
    DotDotEq,
    Comma,
    Colon,
    PathSep,
    RArrow,
    FatArrow,
    Pound,
    Dollar,
    Question,
    Tilde,
    EndOfString,
    Space,
    Lsb,
    Rsb,
    Lp,
    Rp,
}

internal enum KeywordType : int
{
    KwAs,
    KwTo,
    KwEnd,
    KwModule,
    Next,
    KwElse,
    KwStep,
    KwFalse,
    KwSub,
    KwFor,
    KwIf,
    KwDim,
    KwWhile
}