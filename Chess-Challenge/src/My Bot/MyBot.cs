using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using ChessChallenge.API;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class MyBot : IChessBot
{
    Dictionary<ulong, int> boardEvals = new Dictionary<ulong, int>();
    Dictionary<PieceType, int> pieceValues = new Dictionary<PieceType, int>()
    {
        { PieceType.King, 0 },
        { PieceType.Queen, 1000 },
        { PieceType.Rook, 500 },
        { PieceType.Bishop, 330 },
        { PieceType.Knight, 300 },
        { PieceType.Pawn, 100 },
        { PieceType.None, 0 },
    };
    int numberOfTopMovesToTake = 5;

    // Think time in ms
    int turnMaxThinkTime = 50;

    // Accept an eval within 10%
    float maxAcceptableEvalDrift = 0.1f;

    public Move Think(Board board, Timer timer)
    {
        try
        {
            Dictionary<Move, int> moves = new Dictionary<Move, int>();
            Random rng = new();

            var bestMoves = GetBestMoves(board);
            var oppBestMoves = new List<MoveEval>();
            int depth = 0;

            do
            {
                depth++;
                Console.WriteLine($"Evaluating depth {depth} - Total moves: {bestMoves.Count+ oppBestMoves.Count}");
                // if even its our turn
                if (depth % 2 == 0)
                {
                    // for each opp move get the best next moves
                    foreach (var moveEval in oppBestMoves)
                    {
                        // ensure this was from the last set of moves.
                        if (moveEval.depth + 1 == depth)
                        {
                            var moveOrder = GetMoveOrder(moveEval);
                            PerformMoves(board, moveOrder);
                            bestMoves.AddRange(GetBestMoves(board, moveEval));
                            UndoMoves(board, moveOrder);
                            if (timer.MillisecondsElapsedThisTurn > this.turnMaxThinkTime)
                            {
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // for each opp move get the best next moves
                    foreach (var moveEval in bestMoves)
                    {
                        // ensure this was from the last set of moves.
                        if (moveEval.depth + 1 == depth)
                        {
                            var moveOrder = GetMoveOrder(moveEval);
                            PerformMoves(board, moveOrder);
                            // opp moves are gunna be a bit dumb. need a better method
                            oppBestMoves.AddRange(GetBestMoves(board, moveEval, 1));
                            UndoMoves(board, moveOrder);
                            if (timer.MillisecondsElapsedThisTurn > this.turnMaxThinkTime)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            while (timer.MillisecondsElapsedThisTurn < this.turnMaxThinkTime);

            Console.WriteLine($"Total Moves Evaluated: {bestMoves.Count}");

            bestMoves = EvaluateBestMoves(bestMoves, depth);
            var topMoveEval = SelectTopMove(bestMoves, rng);

            Console.WriteLine($"Winning Move {topMoveEval.move.MovePieceType} to {topMoveEval.move.TargetSquare.Name}. eval: {topMoveEval.eval}");
            return topMoveEval.move;
        }
        catch (Exception  e)
        {
            Console.WriteLine(e.ToString());
        }
        return new Move();
    }


    private MoveEval SelectTopMove(List<MoveEval> bestMoves, Random rng)
    {
        var topMoveEval = bestMoves[0];
        var moves = bestMoves.Where(moveEval => moveEval.eval * (1 + this.maxAcceptableEvalDrift) > topMoveEval.eval).ToArray();

        // return all the top moves?
        return moves[rng.Next(moves.Length)];
    }

    private void PerformMoves(Board board, List<Move> moveOrder)
    {
        try
        {
            foreach (var move in moveOrder)
            {
                board.MakeMove(move);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private void UndoMoves(Board board, List<Move> moveOrder)
    {
        try
        {
            moveOrder.Reverse();
            foreach (var move in moveOrder)
            {
                board.UndoMove(move);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private List<Move> GetMoveOrder(MoveEval moveEval)
    {
        var moveOrder = new List<Move>();

        while (moveEval.previousMove != null)
        {
            moveEval = moveEval.previousMove;
            moveOrder.Add(moveEval.move);
        }

        moveOrder.Reverse();

        if (moveOrder.Count > 2)
        {
            return moveOrder;
        }

        return moveOrder;
    }

    // Evaluates the best board moves from a list of moveEvals (different depths)
    private List<MoveEval> EvaluateBestMoves(List<MoveEval> bestMoves, int depth)
    {
        // avg the eval at highest depth and eval up
        MoveEval currentPreviousMoveEval = null;
        int count = 0;
        int sum = 0;

        // if we were adding to opp reduce depth 1 and continue
        if (depth % 2 != 0)
        {
            depth--;
        }

        while (depth > 1)
        {
            foreach(var moveEval in bestMoves)
            {
                if (moveEval.depth == depth)
                {
                    if (currentPreviousMoveEval != moveEval.previousMove.previousMove)
                    {
                        if (currentPreviousMoveEval != null)
                        {
                            currentPreviousMoveEval.eval = sum / count;
                        }

                        currentPreviousMoveEval = moveEval.previousMove.previousMove;
                        sum = moveEval.eval;
                        count = 1;
                    }
                    else
                    {
                        count++;
                        sum += moveEval.eval;
                    }
                }
            }
            depth -= 2;
        }

        return bestMoves
            .Where(moves => moves.depth == 0)
            .OrderByDescending(moves => moves.eval)
            .ToList();
    }

    // Gets the best moves in a board state
    private List<MoveEval> GetBestMoves(Board board, MoveEval previousMove = null, int? movestoTake = null)
    {
        var moves = board.GetLegalMoves();
        var moveEvals = new List<MoveEval>();

        foreach (var move in moves)
        {
            var eval = EvaluateBoard(board, move, previousMove);
            var moveEval = new MoveEval(move, eval, previousMove);
            moveEvals.Add(moveEval);
        }

        return moveEvals
            .OrderByDescending(moveEval => moveEval.eval)
            .Take(movestoTake ?? this.numberOfTopMovesToTake)
            .ToList();
    }

    private int EvaluateBoard(Board board, Move move, MoveEval previousMove)
    {
        try
        {
            //Console.WriteLine($"Move {move.MovePieceType} to {move.TargetSquare.Name}.");

            // TODO get data from move
            //Piece capturedPiece = board.GetPiece(move.TargetSquare);
            //if (capturedPiece != null)
            //{
            //    eval += pieceValues[capturedPiece.PieceType];
            //}

            // TODO move into check below
            //// get number of attacked pieces
            //for (int i = 0; i < 64; i++)
            //{
            //    var square = new Square(i);
            //    if (board.SquareIsAttackedByOpponent(square))
            //    {
            //        // Get value of attacked piece
            //        eval += 0.01 * pieceValues[board.GetPiece(square).PieceType];
            //    }
            //}
            if (move.RawValue == 1624)
            {
                // ????
            }

            board.MakeMove(move);

            double eval = previousMove?.eval * -1 ?? 0;

            if (move.RawValue != 1624)
            {
                // TODO make a dict we can pull previously calculated moves from. Shouldnt be a huge impact
                if (boardEvals.ContainsKey(board.ZobristKey))
                {
                    int val = boardEvals[board.ZobristKey];
                    board.UndoMove(move);
                    return val;
                }

                if (board.IsInCheckmate())
                {
                    eval += 10000;
                }

                if (board.IsInCheck())
                {
                    eval += 20;
                }

                // get number of pieces attacked
                for (int i = 0; i < 64; i++)
                {
                    var square = new Square(i);

                    if (board.SquareIsAttackedByOpponent(square))
                    {
                        eval -= 0.01 * pieceValues[board.GetPiece(square).PieceType];
                    }
                }

                var a = board.IsWhiteToMove;
                var b = a;
                //if (board.TrySkipTurn())
                //{
                //    b = board.IsWhiteToMove;

                //    if (board.SquareIsAttackedByOpponent(move.TargetSquare))
                //    {
                //        eval -= pieceValues[board.GetPiece(move.TargetSquare).PieceType];
                //    }

                //    board.UndoSkipTurn();
                //}

                //Console.WriteLine($"Move {move.MovePieceType} to {move.TargetSquare.Name}. eval: {(int) eval}");

                boardEvals.Add(board.ZobristKey, (int) eval);
            }

            board.UndoMove(move);
            return (int) eval;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        return 0;
    }

    private class MoveEval
    {
        public Move move;
        public int eval;
        public int depth;
        public MoveEval previousMove;

        public MoveEval(Move move, int eval, MoveEval previousMove = null)
        {
            this.move = move;
            this.eval = eval;
            this.previousMove = previousMove;
            this.depth = previousMove?.depth+1 ?? 0;
        }
    }
}