using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public sealed class CBSyntaxException : Exception
{
    public readonly int ErrorPosition;
    public CBSyntaxException(string message, int spos) : base(message)
    {
        ErrorPosition = spos;
    }
}

public sealed class ConditionalBlocks
{
    private readonly HashSet<string> CompileTimeTokens = new HashSet<string>();

    private int SourcePosition;

    public void LoadConditionalTokens(List<string> tokens)
    {
        CompileTimeTokens.Clear();
        foreach (var token in tokens)
            CompileTimeTokens.Add(token.ToLowerInvariant().Trim());
    }

    private enum RBCBlockState
    {
        NoCondition,
        FailsCondition,
        MeetsCondition
    }

    private enum RBCParseState
    {
        Ready,
        String,
        BlockComment,
        LineComment
    }

    private enum RCBBlockType
    {
        IFDEF,
        IFNDEF,
        ELSE,
        ENDIF
    }

    private RBCParseState ParseState;
    private RBCBlockState BlockState;
    private Stack<RBCBlockState> PrevConditions = new Stack<RBCBlockState>();
    private readonly Stack<bool> ElseSeen = new Stack<bool>();
    private char[] InPlace;

    public string ParseSource(string input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        ParseState = RBCParseState.Ready;
        BlockState = RBCBlockState.NoCondition;
        PrevConditions.Clear();
        ElseSeen.Clear();
        InPlace = input.ToCharArray();

        Regex regex = new Regex(@"^(#ifdef|#ifndef|#else|#endif)\b");
        ReadOnlySpan<char> CurrString;

        for (SourcePosition = 0; SourcePosition < input.Length; SourcePosition++)
        {
            switch (ParseState)
            {
                case RBCParseState.Ready:
                    if (input[SourcePosition] == '"')
                        ParseState = RBCParseState.String;
                    else if (input[SourcePosition] == '/')
                    {
                        if (SourcePosition + 1 >= input.Length)
                            break;

                        if (input[SourcePosition + 1] == '*')
                            ParseState = RBCParseState.BlockComment;
                        else if (input[SourcePosition + 1] == '/')
                            ParseState = RBCParseState.LineComment;
                    }
                    else if (input[SourcePosition] == '#')
                    {
                        CurrString = input.AsSpan().Slice(SourcePosition, Math.Min(10, input.Length - SourcePosition));
                        var resultantstring = CurrString.ToString();
                        if (!regex.IsMatch(resultantstring)) //there is no clean way around this sadly
                            break;

                        var match = regex.Match(resultantstring);
                        InterpretMatch(match);
                        SourcePosition--;
                        continue;
                    }
                    break;
                case RBCParseState.String:
                    if (input[SourcePosition] == '"')
                    {
                        int escapes = 0;
                        for (int i = SourcePosition - 1; i >= 0 && input[i] == '\\'; i--) escapes++;
                        if (escapes % 2 == 0) ParseState = RBCParseState.Ready;
                    }
                    break;
                case RBCParseState.BlockComment:
                    if (input[SourcePosition] == '*' && input.Length > SourcePosition + 1 && input[SourcePosition + 1] == '/')
                        ParseState = RBCParseState.Ready;
                    break;
                case RBCParseState.LineComment:
                    if (input[SourcePosition] == '\r' || input[SourcePosition] == '\n')
                        ParseState = RBCParseState.Ready;
                    break;
            }

            if (BlockState == RBCBlockState.FailsCondition && InPlace[SourcePosition] != '\n')
                InPlace[SourcePosition] = ' ';
        }

        if (PrevConditions.Count > 0)
            throw new CBSyntaxException("Expected #endif", SourcePosition);

        return new string(InPlace);
    }

    private void InterpretMatch(Match match)
    {
        string mv = match.Value.Substring(1);

        Enum.TryParse<RCBBlockType>(mv, true, out var result);

        bool isTerminator = (result == RCBBlockType.ELSE || result == RCBBlockType.ENDIF);
        bool isIf = result == RCBBlockType.IFDEF;

        if (isTerminator && (PrevConditions.Count < 1 || BlockState == RBCBlockState.NoCondition))
            throw new CBSyntaxException($"Extraneous {result.ToString().ToLowerInvariant()}", SourcePosition);

        ReplaceRange(match.Value.Length);

        if (isTerminator)
        {
            if (result == RCBBlockType.ELSE)
            {
                if (ElseSeen.Pop())
                    throw new CBSyntaxException("Duplicate #else", SourcePosition);
                ElseSeen.Push(true);
                if (IsChainNegated())
                    PrevConditions.Push(RBCBlockState.FailsCondition);
                else
                    PrevConditions.Push(BlockState == RBCBlockState.MeetsCondition ? RBCBlockState.FailsCondition : RBCBlockState.MeetsCondition);
            }
            else
            {
                ElseSeen.Pop();
            }

            BlockState = PrevConditions.Pop();
            return;
        }


        if (!TryFindToken(out string Token))
            throw new CBSyntaxException($"Expected identifier", SourcePosition);

        SourcePosition -= Token.Length;
        ReplaceRange(Token.Length);

        PrevConditions.Push(BlockState);
        ElseSeen.Push(false);

        if (BlockState == RBCBlockState.FailsCondition)
            return;

        BlockState = (CompileTimeTokens.Contains(Token) == isIf) ? RBCBlockState.MeetsCondition : RBCBlockState.FailsCondition;
    }

    private bool IsChainNegated()
    {
        foreach (var condition in PrevConditions)
            if (condition == RBCBlockState.FailsCondition)
                return true;
        return false;
    }

    private bool TryFindToken(out string Token)
    {
        Token = null;
        StringBuilder tb = new StringBuilder();

        while (SourcePosition < InPlace.Length && char.IsWhiteSpace(InPlace[SourcePosition]))
            SourcePosition++; //safely ignore whitespace

        if (SourcePosition >= InPlace.Length)
            return false;

        if (!char.IsLetterOrDigit(InPlace[SourcePosition]) && InPlace[SourcePosition] != '_')
            return false;

        tb.Append(InPlace[SourcePosition++]);

        while (SourcePosition < InPlace.Length && (char.IsLetterOrDigit(InPlace[SourcePosition]) || InPlace[SourcePosition] == '_'))
            tb.Append(InPlace[SourcePosition++]);

        Token = tb.ToString().ToLowerInvariant();

        return true;
    }

    private void ReplaceRange(int length)
    {
        for (int i = 0; i < length; i++)
            if (InPlace[i + SourcePosition] != '\n')
                InPlace[i + SourcePosition] = ' ';

        SourcePosition += length;
    }
}

