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
    int numberOfTopMovesToTake = 20;

    // Think time in ms
    int turnMaxThinkTime = 50;
    int maxDepth = 10;
    // Accept an eval within top 5%
    float maxAcceptableEvalDrift = 0.05f;

    public Move Think(Board board, Timer timer)
    {
        try
        {
            Random rng = new();
            this.turnMaxThinkTime = rng.Next(2)^2;
            Dictionary<Move, int> moves = new Dictionary<Move, int>();
            boardEvals.Clear();
            var bestMoves = new List<List<MoveEval>>();
            bestMoves.Add(GetBestMoves(board));
            bestMoves.Add(new List<MoveEval>());
            int depth = 0;

            do
            {
                depth++;
                // if even its our turn; index 0 is our moves
                var index = depth % 2;

                if (index == 0)
                {
                    //Console.WriteLine($"Evaluating depth {depth/2} - Total moves: {bestMoves[0].Count + bestMoves[1].Count}");
                }
                // for each opp move get the best next moves
                foreach (var moveEval in bestMoves[(index + 1) % 2])
                {
                    // ensure this was from the last set of moves.
                    if (moveEval.depth + 1 == depth)
                    {
                        var moveOrder = GetMoveOrder(moveEval);
                        PerformMoves(board, moveOrder);
                        bestMoves[index].AddRange(GetBestMoves(board, moveEval, this.numberOfTopMovesToTake * (2 / depth + 1)));
                        UndoMoves(board, moveOrder);
                        if (timer.MillisecondsElapsedThisTurn > this.turnMaxThinkTime)
                        {
                            break;
                        }
                    }
                }

                var numMoves = bestMoves[0].Count + bestMoves[1].Count;
                //Console.WriteLine($"Evaluating depth {depth} - Total moves: {numMoves}");
                EnsureMovesAreDistinct(ref bestMoves, depth);
                bestMoves[index] = EvaluateBestMovesV2(bestMoves[index], depth, index == 1);
                WeedOutBadMoves(ref bestMoves, depth);
                var removed = numMoves - (bestMoves[0].Count + bestMoves[1].Count);
                //Console.WriteLine($"Removed {removed} moves.");
            }
            while (timer.MillisecondsElapsedThisTurn < this.turnMaxThinkTime && depth < maxDepth);

            var movesEvaluated = bestMoves[0].Count + bestMoves[1].Count;

            var ourBestMoves = bestMoves[0]
                .Where(moves => moves.depth == 0)
                .ToList();

            var topMoveEval = SelectTopMove(ourBestMoves, rng);

            var theirBestMoves = bestMoves[1]
                .Where(moves => moves.depth == 1)
                .ToList();

            if (topMoveEval == null)
            {
                topMoveEval = theirBestMoves.FirstOrDefault().previousMove;
            }

            theirBestMoves = theirBestMoves
                .Where(moves => moves.previousMove == topMoveEval)
                .ToList();

            var oppTopMoveEval = SelectTopMove(theirBestMoves, rng);

            //Console.WriteLine($"Time to evaluate: {timer.MillisecondsElapsedThisTurn.ToString("N0")} ms");
            Console.WriteLine($"Total Moves Evaluated: {movesEvaluated}");
            Console.WriteLine($"Winning Move: {topMoveEval.move.MovePieceType} to {topMoveEval.move.TargetSquare.Name}. eval: {topMoveEval.eval.ToString("N2")}");
            //Console.WriteLine($"Expected oppenent move: {oppTopMoveEval?.move.MovePieceType} to {oppTopMoveEval?.move.TargetSquare.Name}. eval: {oppTopMoveEval?.eval.ToString("N2")}");

            if (topMoveEval.eval < -100)
            {
                //why are we picking a bad move da fuq
            }
            //Console.WriteLine($"");
            return topMoveEval.move;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        return new Move();
    }


    private MoveEval SelectTopMove(List<MoveEval> bestMoves, Random rng)
    {
        if (!bestMoves.Any())
        {
            // wtf
            return null;
        }
        var topMoveEval = bestMoves[0];
        var moves = bestMoves.Where(moveEval => IsWithinEvalTolerance(moveEval.eval, topMoveEval.eval, this.maxAcceptableEvalDrift)).ToArray();

        //if (!moves.Any()) 
        //{
        //    return topMoveEval;
        //}

        // return all the top moves?
        return moves[rng.Next(moves.Length)];
    }

    private void PerformMoves(Board board, List<Move> moveOrder)
    {
        foreach (var move in moveOrder)
        {
            board.MakeMove(move);
        }
    }

    private void UndoMoves(Board board, List<Move> moveOrder)
    {
        moveOrder.Reverse();
        foreach (var move in moveOrder)
        {
            board.UndoMove(move);
        }
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

    private void WeedOutBadMoves(ref List<List<MoveEval>> bestMoves, int maxDepth)
    {
        var removed = false;

        // alternate back and forth and weed out tolerance failing moves and eliminate all moves downstream
        for (int depth = 0; depth < 2; depth++)
        {
            if (depth > maxDepth)
            {
                return;
            }

            var moves = bestMoves[depth % 2].Where(moves => moves.depth == depth);

            foreach (var moveSet in moves.GroupBy(move => move.previousMove))
            {
                if ( RemoveBadMoves(moveSet, ref bestMoves, maxDepth))
                {
                    removed = true;
                }
            }
        }
    }

    private bool RemoveBadMoves(IEnumerable<MoveEval> moves, ref List<List<MoveEval>> bestMoves, int depth)
    {
        var tolerance = 2 / (depth+1);
        moves = moves.OrderByDescending(move => move.eval);
        var topMove = moves.FirstOrDefault().eval;
        moves.Reverse();
        foreach (var move in moves)
        {
            // only analyze the highest level moves.
            if (!IsWithinEvalTolerance(move.eval + 10, topMove, tolerance))
            {
                // Found a looser
                RemoveMovesRelatedTo(move, ref bestMoves);
                return true;
            }
        }

        return false;
    }

    private void RemoveMovesRelatedTo(MoveEval move, ref List<List<MoveEval>> bestMoves)
    {
        var index = (move.depth + 1) % 2;

        var movesToRemove = bestMoves[index].Where(move => move.previousMove == move);

        if (movesToRemove.Any())
        {
            foreach (var moveEval in movesToRemove)
            {
                RemoveMovesRelatedTo(moveEval, ref bestMoves);
            }
        }

        bestMoves[(index + 1) % 2].Remove(move);
    }
    // Evaluates the best board moves from a list of moveEvals (different depths)
    private List<MoveEval> EvaluateBestMoves(List<MoveEval> bestMoves, int depth, bool isOpp = false)
    {
        // avg the eval at highest depth and eval up
        MoveEval currentPreviousMoveEval = null;
        int count = 0;
        double sum = 0;

        // if we were adding to opp reduce depth 1 and continue
        if (depth % 2 != 0 && !isOpp)
        {
            depth--;
        }

        while (depth > 1)
        {
            foreach (var moveEval in bestMoves)
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
            .OrderByDescending(moves => moves.eval)
            .ToList();
    }

    // Evaluates the best board moves from a list of moveEvals (different depths)
    private List<MoveEval> EvaluateBestMovesV2(List<MoveEval> bestMoves, int depth, bool isOpp = false)
    {
        // avg the eval at highest depth and eval up

        while (depth > 1)
        {
            var groupings = bestMoves.Where(move => move.depth == depth).GroupBy(move => move.previousMove.previousMove);

            foreach (var grouping in groupings)
            {
                int count = 0;
                double sum = 0;

                foreach (var move in grouping)
                {
                    sum += move.eval;
                    count++;
                }
                grouping.FirstOrDefault().previousMove.previousMove.eval = sum / count;
            }
            depth -= 2;
        }

        return bestMoves
            .OrderBy(moves => moves.depth)
            .ThenByDescending(moves => moves.eval)
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
            board.MakeMove(move);
            var moveEval = new MoveEval(move, eval, board.ZobristKey, previousMove);
            board.UndoMove(move);
            moveEvals.Add(moveEval);
        }

        moveEvals = moveEvals
            .OrderByDescending(moveEval => moveEval.eval)
            .Take(movestoTake ?? this.numberOfTopMovesToTake)
            .ToList();

        return moveEvals;
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