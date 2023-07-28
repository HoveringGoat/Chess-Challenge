using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

public class MyBotLast : IChessBot
{
    Dictionary<ulong, double> boardEvals = new Dictionary<ulong, double>();
    Dictionary<PieceType, int> pieceValues = new Dictionary<PieceType, int>()
    {
        { PieceType.King, 1000 },
        { PieceType.Queen, 1000 },
        { PieceType.Rook, 500 },
        { PieceType.Bishop, 330 },
        { PieceType.Knight, 300 },
        { PieceType.Pawn, 100 },
        { PieceType.None, 0 },
    };

    // Think time in ms
    //int turnMaxThinkTime = 50;

    public Move Think(Board board, Timer timer)
    {
        Random rng = new();
        var legalMoves = board.GetLegalMoves(false);
        foreach (var move in legalMoves)
        {
            if (move.IsCapture)
            {
                return move;
            }
            if (move.IsEnPassant)
            {
                return move;
            }
            if (move.IsPromotion)
            {
                return move;
            }
            if (move.IsCastles)
            {
                return move;
            }
            board.MakeMove(move);
            var checkmate = board.IsInCheckmate();
            board.UndoMove(move);

            if(checkmate)
            {
                return move;
            }
        }

        return legalMoves[rng.Next(legalMoves.Length)];
    }



    private List<Move> GetMoveOrder(MoveEval moveEval)
    {
        var moveOrder = new List<Move>();

        while (moveEval != null)
        {
            moveOrder.Add(moveEval.move);
            moveEval = moveEval.previousMove;
        }

        moveOrder.Reverse();

        return moveOrder;
    }

    private void EnsureMovesAreDistinct(ref List<List<MoveEval>> bestMoves, int maxDepth)
    {

        for (int i = 0; i < 2; i++)
        {
            bestMoves[i] = bestMoves[i].DistinctBy(move => move.boardZobristKey).ToList();
        }
    }


    private bool IsWithinEvalTolerance(double eval, double maxEval, double tolerance)
    {
        if (eval < 0)
        {
            var inc = Math.Abs(eval) * 1.5;
            maxEval += inc;
            eval += inc;
        }

        var adjustedEval = eval * (1 + tolerance) + 5;

        var isWithinTolerance = adjustedEval > maxEval;
        return isWithinTolerance;
    }

    private double EvaluateBoard(Board board, Move move, MoveEval previousMove)
    {
        try
        {
            //Console.WriteLine($"Move {move.MovePieceType} to {move.TargetSquare.Name}.");
            var lastLegalMoves = board.GetLegalMoves().Length;
            board.MakeMove(move);

            if (board.IsDraw() || board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)
            {
                board.UndoMove(move);
                return 0;
            }

            if (boardEvals.ContainsKey(board.ZobristKey))
            {
                var val = boardEvals[board.ZobristKey];
                board.UndoMove(move);
                return val;
            }

            // neutral eval is +10 - prevents minor negatives from getting auto discarded
            // this somewhat counters that we heavily penalize allowing opp moves
            // TODO figure out value from pieces+pos instead of grandfathering values in (that might not be accurate)
            double eval = previousMove?.eval * -.25 ?? 10; 

            // get number of pieces attacked
            if (board.IsInCheckmate())
            {
                eval += 10000;
            }

            if (move.IsCapture)
            {
                eval += pieceValues[move.CapturePieceType];
            }

            if (move.IsCastles)
            {
                eval += 50;
            }
            if (move.IsPromotion)
            {
                eval += 700;
            }
            if (move.MovePieceType == PieceType.King)
            {
                eval -= 5;
            }
            if (move.MovePieceType == PieceType.Pawn)
            {
                // is pass pawn
                //bitboards dont make sense
                //var oppPawnBitBoard = board.GetPieceBitboard(PieceType.Pawn, board.IsWhiteToMove);
                var pawns = board.GetPieceList(PieceType.Pawn, board.IsWhiteToMove);
                if (!pawns.Any(pawn => pawn.Square.File == move.TargetSquare.File))
                {
                    eval += 25;
                }
            }

            // Check current opp moves and if they can capture the piece we just moved
            var oppMoves = board.GetLegalMoves();

            eval -= (oppMoves.Length * .05f);
            foreach (var oppMove in oppMoves)
            {
                if (oppMove.IsCapture)
                {
                    // only count the capture full points if its what we just moved. Otherwise its probably okay?
                    if (oppMove.TargetSquare == move.TargetSquare)
                    {
                        eval -= pieceValues[oppMove.CapturePieceType];
                    }

                    eval -= pieceValues[oppMove.CapturePieceType] * .05;
                }
                board.MakeMove(oppMove);
                if (board.IsInCheckmate())
                {
                    eval -= 10000;
                }
                board.UndoMove(oppMove);
            }

            board.MakeMove(Move.NullMove);

            // more moves available is better
            var legalMoves = board.GetLegalMoves();
            eval += (legalMoves.Length - lastLegalMoves) * .05f;

            foreach (var nextMove in legalMoves)
            {
                // attacking pieces is good
                if (nextMove.IsCapture)
                {
                    eval += pieceValues[nextMove.CapturePieceType] * .05;
                }
            }
            board.UndoMove(Move.NullMove);

            //Console.WriteLine($"Move {move.MovePieceType} to {move.TargetSquare.Name}. eval: {eval}");

            // game is drawn

            boardEvals.Add(board.ZobristKey, eval);

            board.UndoMove(move);
            return eval;
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
        public double eval;
        public ulong boardZobristKey;
        public int depth;
        public MoveEval previousMove;

        public MoveEval(Move move, double eval, ulong boardZobristKey, MoveEval previousMove = null)
        {
            this.move = move;
            this.eval = eval;
            this.boardZobristKey = boardZobristKey;
            this.previousMove = previousMove;
            this.depth = previousMove?.depth + 1 ?? 0;
        }
    }
}