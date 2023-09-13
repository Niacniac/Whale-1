using System;
using static System.Math;
using Chess;
using System.Globalization;

public class Search
{
    // Constants
    const int transpositionTableSize = 4000;
    const int maxExtentions = 16;
    public int aspirationWindows = 9;
    public float aspirationWindowsPowerMultiplicator = 0.6f;
    const int immediateMateScore = 100000;
    const int positiveInfinity = 9999999;
    const int negativeInfinity = -positiveInfinity;

    const int maxNullMoveR = 4;
    const int minNullMoveR = 4;
    const int nullMoveDepthReduction = 4;

    public event Action<Move>? OnSearchComplete;

    // State
    public int currentDepth;
    public Move BestMoveSoFar => bestMove;
    public int BestEvalSoFar => bestEval;
    bool isPlayingWhite;
    Move bestMoveThisIteration;
    int bestEvalThisIteration;
    Move bestMove;
    int bestEval;
    bool hasSearchedAtLeastOneMove;
    bool abortSearch;
    int[] pvLength;
    Move[,] pvTable;
    int LastIterationEval; // Used in case where there is no move inside the aspiration windows
    uint age;

    // References
    Board board;
    RepetitionTable repetitionTable;
    MoveGenerator moveGenerator;
    TranspositionTable tTable;
    Evaluation evaluation;
    MoveOrdering moveOrdering;
    int currentIterativeSearchDepth;

    // Diagnostics
    public SearchDiagnostics searchDiagnostics;
    int currentIterationDepth;
    System.Diagnostics.Stopwatch searchIterationTimer;
    System.Diagnostics.Stopwatch searchTotalTimer;
    public string debugInfo;
    

    public Search(Board board)
    {
        this.board = board;

        evaluation = new Evaluation();
        moveGenerator = new MoveGenerator();
        tTable = new TranspositionTable(transpositionTableSize);
        moveOrdering = new MoveOrdering(moveGenerator, tTable);
        repetitionTable = new RepetitionTable();

        moveGenerator.promotionsToGenerate = MoveGenerator.PromotionMode.All;

        pvLength = new int[272];
        pvTable = new Move[272, 272];
        age = 0;

        SearchMoves(1, 0, negativeInfinity, positiveInfinity);
    }
    public void StartSearch()
    {
        bestEvalThisIteration = bestEval = 0;
        bestMoveThisIteration = bestMove = Move.NullMove;

        isPlayingWhite = board.IsWhiteToMove;

        moveOrdering.ClearHistory();
        repetitionTable.Init(board);

        // Initialize debug info
        currentDepth = 0;
        debugInfo = "Starting search with FEN " + FenUtility.CurrentFen(board);
        abortSearch = false;
        searchDiagnostics = new SearchDiagnostics();
        searchIterationTimer = new System.Diagnostics.Stopwatch();
        searchTotalTimer = System.Diagnostics.Stopwatch.StartNew();



        // Iterative deepening
        RunIterativeDeepeningSearch();



        if (bestMove.IsNull)
        {
            bestMove = moveGenerator.GenerateMoves(board)[0];
        }
        OnSearchComplete?.Invoke(bestMove);
        abortSearch = false;
    }

    public (Move move, int eval) GetSearchResult()
    {
        return (bestMove, bestEval);
    }

    public void EndSearch()
    {
        abortSearch = true;
    }

   


    public static bool IsMateScore(int score)
    {
        const int maxMateDepth = 1000;
        return Abs(score) > immediateMateScore - maxMateDepth;
    }

    void RunIterativeDeepeningSearch()
    {
        int a = negativeInfinity;
        int b = positiveInfinity;
        int alphaAspirationWindowsFailed = 0;
        int betaAspirationWindowsFailed = 0;

        for (int searchDepth = 1; searchDepth <= 220; searchDepth++)
        {
            hasSearchedAtLeastOneMove = false;
            debugInfo += "\nStarting Iteration: " + searchDepth;
            searchIterationTimer.Restart();
            currentIterationDepth = searchDepth;

            LastIterationEval = SearchMoves(searchDepth, 0, a, b);

            if (abortSearch)
            {
                if (hasSearchedAtLeastOneMove)
                {
                    bestMove = bestMoveThisIteration;
                    bestEval = bestEvalThisIteration;
                    searchDiagnostics.move = MoveUtility.GetMoveNameUCI(bestMove);
                    searchDiagnostics.eval = bestEval;
                    searchDiagnostics.moveIsFromPartialSearch = true;
                    debugInfo += "\nUsing partial search result: " + MoveUtility.GetMoveNameUCI(bestMove) + " Eval: " + bestEval;

                }
                debugInfo += "\nSearch aborted";
                break;
            }
            else
            {
                age++;

                int alphaIncrement;
                int betaIncrement;
                if (LastIterationEval <= a || LastIterationEval >= b)
                {
                    if (LastIterationEval <= a)
                    {
                        alphaAspirationWindowsFailed++;
                    }
                    else
                    {
                        betaAspirationWindowsFailed++;
                    }
                    alphaIncrement = (int)Pow(aspirationWindows, (alphaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                    betaIncrement = (int)Pow(aspirationWindows, (betaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                    a = LastIterationEval - alphaIncrement;
                    b = LastIterationEval + betaIncrement;
                    searchDepth--;

                    continue;
                }

                alphaAspirationWindowsFailed = 0;
                betaAspirationWindowsFailed = 0;
                alphaIncrement = (int)Pow(aspirationWindows, (alphaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                betaIncrement = (int)Pow(aspirationWindows, (betaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                a = bestEvalThisIteration - alphaIncrement;
                b = bestEvalThisIteration + betaIncrement;


                currentDepth = searchDepth;
                bestMove = bestMoveThisIteration;
                bestEval = bestEvalThisIteration;
                
                string pvLineName = "";
                for (int count = 0; count < pvLength[0]; count++)
                {
                    pvLineName += MoveUtility.GetMoveNameUCI(pvTable[0, count]).Replace("=", "") + " ";
                }

                searchIterationTimer.Stop();
                var timeElapsedInIteration = searchIterationTimer.Elapsed;
                var totalElapsedTime = searchTotalTimer.Elapsed;
                float totalElapsedTimeSecond = totalElapsedTime.Seconds + totalElapsedTime.Milliseconds/1000f;
                float nps = searchDiagnostics.numNodes / totalElapsedTimeSecond;
                if(nps > Int32.MaxValue || nps != nps)
                {
                    nps = 0f;
                }
                int nodesPerSecond = Convert.ToInt32(nps);
                Console.WriteLine($"info depth {currentDepth} score cp {bestEval} nodes {searchDiagnostics.numNodes} nps {nodesPerSecond} time {timeElapsedInIteration.Milliseconds + timeElapsedInIteration.Seconds*1000} pv {pvLineName}");
                // Update diagnostics
                debugInfo += "\nIteration result: " + MoveUtility.GetMoveNameUCI(bestMove) + " Eval: " + bestEval;
                if (IsMateScore(bestEval))
                {
                    debugInfo += " Mate in ply: " + NumPlyToMateFromScore(bestEval);
                }
                bestEvalThisIteration = int.MinValue;
                bestMoveThisIteration = Move.NullMove;

                // Update diagnostics
                searchDiagnostics.numCompletedIterations = searchDepth;
                searchDiagnostics.move = MoveUtility.GetMoveNameUCI(bestMove);
                searchDiagnostics.eval = bestEval;
                // Exit search if found a mate within search depth.
                // A mate found outside of search depth (due to extensions) may not be the fastest mate.
                if (IsMateScore(bestEval) && NumPlyToMateFromScore(bestEval) <= searchDepth)
                {
                    debugInfo += "\nExitting search due to mate found within search depth";
                    break;
                }
            }
        }
    }

    

    // Main search function
    int SearchMoves(int depth, int plyFromRoot, int alpha, int beta, int numExtensions = 0, Move prevMove = default, bool prevWasCapture = false, bool doNull = true)
    {
        if (abortSearch)
        {
            return 0;
        }

        // init PV length
        pvLength[plyFromRoot] = plyFromRoot;


        if (plyFromRoot > 0)
        {
            // detect draw by repetition
            if (board.CurrentGameState.fiftyMoveCounter >= 100 || repetitionTable.Contains(board.CurrentGameState.zobristKey))
            {
                return 0;
            }

            // If a mating sequence as already been found this position is skipped
            alpha = Max(alpha, -immediateMateScore + plyFromRoot);
            beta = Min(beta, immediateMateScore - plyFromRoot);
            if (alpha >= beta)
            {
                return alpha;
            }
        }

        
        // Null move prunning
        if (depth >= 3 && !board.IsInCheck() && plyFromRoot > 0 && doNull)
        {
            board.MakeNullMove();
            int R = depth > 6 ? maxNullMoveR : minNullMoveR;
            int evaluation = -SearchMoves(depth - R - 1, plyFromRoot + 1, -beta, -beta + 1, doNull:false);
            board.UnmakeNullMove();

            if (abortSearch)
            {
                return 0;
            }
            if (evaluation >= beta)
            {
                depth -= nullMoveDepthReduction;
                if (depth <= 0)
                {
                    return QuiescenceSearch(alpha, beta, plyFromRoot);
                }
            }
        }
        
        
        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(board, depth, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed)
        {
            if (plyFromRoot == 0)
            {
                bestMoveThisIteration = tTable.GetStoredMove(board);
                bestEvalThisIteration = tTable.GetStoredScore(board);
                //Debug.Log ("move retrieved " + bestMoveThisIteration.Name + " Node type: " + tt.entries[tt.Index].nodeType + " depth: " + tt.entries[tt.Index].depth);
            }
            return ttVal;
        }

        if (depth <= 0)
        {
            return QuiescenceSearch(alpha, beta, plyFromRoot);
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        moveGenerator.GenerateMoves(board, ref moves, capturesOnly: false);
        Move prevBestMove = plyFromRoot == 0 ? bestMove : tTable.GetStoredMove(board);
        moveOrdering.OrderMoves(prevBestMove, board, moves, moveGenerator.opponentAttackMap, moveGenerator.opponentPawnAttackMap, false, plyFromRoot);
        if (moves.Length == 0)
        {
            if (moveGenerator.InCheck())
            {
                int mateScore = immediateMateScore - plyFromRoot;
                return -mateScore; // Checkmate
            }
            else
            {
                return 0; // Stalemate
            }
        }

        if (plyFromRoot > 0)
        {
            bool wasPawnMove = Piece.PieceType(board.Square[prevMove.TargetSquare]) == Piece.Pawn;
            repetitionTable.Push(board.CurrentGameState.zobristKey, prevWasCapture || wasPawnMove);
        }

        int evalType = TranspositionTable.UpperBound;
        Move bestMoveInThisPosition = Move.NullMove;

        for (int i = 0; i < moves.Length; i++)
        {
            int capturedPieceType = Piece.PieceType(board.Square[moves[i].TargetSquare]);
            bool isCapture = capturedPieceType != Piece.None;
            board.MakeMove(moves[i], true);

            // Move extension : search interesting board to a deeper depth
            int extension = 0;
            if (numExtensions < maxExtentions)
            {
                int movedPieceType = Piece.PieceType(board.Square[moves[i].TargetSquare]);
                int targetRank = BoardHelper.RankIndex(moves[i].TargetSquare);
                if (board.IsInCheck())
                {
                    extension = 1;
                }
                else if (movedPieceType == Piece.Pawn && (targetRank == 1 || targetRank == 6))
                {
                    extension = 1;
                }
            }


            // PV Search
            int eval;
            if (i == 0)
            {
                eval = -SearchMoves(depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture);
            }
            else
            {
                // Late move reduction
                int reduction = 0;
                
                if (depth >= 2 && !isCapture && extension == 0 && i > 3)
                {
                    if (depth <= 6)
                    {
                        reduction = 1;
                    }
                    else
                    {
                        reduction = depth / 3;
                    }

                }
                eval = -SearchMoves(depth - 1 - reduction, plyFromRoot + 1, -alpha - 1, -alpha, numExtensions, moves[i], isCapture);
                if (eval > alpha)
                {
                    eval = -SearchMoves(depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture);
                }
            }

            board.UnmakeMove(moves[i], true);

            if (abortSearch)
            {
                return 0;
            }
            searchDiagnostics.numNodes++;

            if (eval >= beta)
            {
                tTable.StoreEvaluation(board, depth, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i], age);

                // Update killer moves and history heuristic (note: don't include captures as theres are ranked highly anyway)
                if (!isCapture)
                {
                    if (plyFromRoot < MoveOrdering.maxKillerMovePly)
                    {
                        moveOrdering.killerMoves[plyFromRoot].Add(moves[i]);
                    }
                    int historyScore = depth * depth;
                    moveOrdering.History[board.MoveColourIndex, moves[i].StartSquare, moves[i].TargetSquare] += historyScore;
                }
                if (plyFromRoot > 0)
                {
                    repetitionTable.TryPop();
                }

                searchDiagnostics.numCutOffs++;
                return beta;
            }


            if (eval > alpha)
            {
                evalType = TranspositionTable.Exact;
                bestMoveInThisPosition = moves[i];

                alpha = eval;

                // write PV move
                pvTable[plyFromRoot, plyFromRoot] = bestMoveInThisPosition;

                // loop over the next ply
                for (int nextPly = plyFromRoot + 1; nextPly < pvLength[plyFromRoot + 1]; nextPly++)
                {
                    // copy move from deeper ply into current ply's line
                    pvTable[plyFromRoot, nextPly] = pvTable[plyFromRoot + 1, nextPly];
                }
                pvLength[plyFromRoot] = pvLength[plyFromRoot + 1];

                if (plyFromRoot == 0)
                {
                    bestMoveThisIteration = moves[i];
                    bestEvalThisIteration = eval;

                }
            }
        }

        if (plyFromRoot > 0)
        {
            repetitionTable.TryPop();
        }


        tTable.StoreEvaluation(board, depth, plyFromRoot, alpha, evalType, bestMoveInThisPosition, age);

        return alpha;
    }

    // Search to a further depth until a stable position is reached (stable means no capture left)
    int QuiescenceSearch(int alpha, int beta, int plyFromRoot)
    {
        if (abortSearch)
        {
            return 0;
        }

        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(board, 0, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed)
        {
            return ttVal;
        }


        int eval = evaluation.Evaluate(board);
        searchDiagnostics.numPositionsEvaluated++;
        if (eval >= beta)
        {
            searchDiagnostics.numCutOffs++;
            return beta;
        }
        if (eval > alpha)
        {
            alpha = eval;
        }

        Span<Move> moves = stackalloc Move[128];
        int evalType = TranspositionTable.UpperBound;
        moveGenerator.GenerateMoves(board, ref moves, capturesOnly: true);
        moveOrdering.OrderMoves(Move.NullMove, board, moves, moveGenerator.opponentAttackMap, moveGenerator.opponentPawnAttackMap, true, 0);
        for (int i = 0;i < moves.Length;i++)
        {
            board.MakeMove(moves[i], true);
            eval = -QuiescenceSearch(-beta, -alpha, plyFromRoot + 1);
            board.UnmakeMove(moves[i], true);

            searchDiagnostics.numNodes++;

            if (eval >= beta)
            {
                tTable.StoreEvaluation(board,0, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i], age);
                searchDiagnostics.numCutOffs++;
                return beta;
            }
            if (eval > alpha)
            {
                evalType = TranspositionTable.Exact;
                alpha = eval;
            }
        }
        tTable.StoreEvaluation(board, 0, plyFromRoot, alpha, evalType, Move.NullMove, age);

        return alpha;
    }


    public static int NumPlyToMateFromScore(int score)
    {
        return immediateMateScore - Abs(score);

    }

    public void ClearForNewPosition()
    {
        tTable.Clear();
        moveOrdering.ClearKillers();
        age = 0;
    }

    [Serializable]
    public struct SearchDiagnostics
    {
        public int numCompletedIterations;
        public int numPositionsEvaluated;
        public int numNodes;
        public ulong numCutOffs;

        public string moveVal;
        public string move;
        public int eval;
        public bool moveIsFromPartialSearch;
        public int NumQChecks;
        public int numQMates;

        public bool isBook;

        public int maxExtentionReachedInSearch;
    }
}
