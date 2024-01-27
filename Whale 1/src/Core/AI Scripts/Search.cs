using static System.Math;
using System.Diagnostics;


public class Search
{
    // Constants
    const int maxThreads = 32;
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
    const bool allowNNUE = true;
    public event Action<Move>? OnSearchComplete;

    // State
    //public int currentDepth;

    //Move bestMoveThisIteration;
    //int bestEvalThisIteration;
    //Move bestMove;
    //int bestEval;
    bool hasSearchedAtLeastOneMove;
    bool abortSearch;
    //int[] pvLength;
    //Move[,] pvTable;
    //int LastIterationEval; // Used in case where there is no move inside the aspiration windows
    uint age;

    // References
    Board board;
    TranspositionTable tTable;
    int currentIterativeSearchDepth;

    // Thread
    int threadNumber = 1;
    ThreadWorkerData[] threadWorkerDatas;
    // Diagnostics
    int currentIterationDepth;
    Stopwatch searchTotalTimer;
    public string debugInfo;
    



    public Search(Board board)
    {
        this.board = board;
        tTable = new TranspositionTable(transpositionTableSize);
        InitWorkersDatas(threadNumber);
        age = 0;
    }
    public void StartSearch()
    {
        // Init data to Search
        for (int i = 0; i < threadWorkerDatas.Length; i++)
        {
            threadWorkerDatas[i].moveOrdering.ClearHistory();
            threadWorkerDatas[i].repetitionTable.Init(board);
        }
        UpdateWorkersDatas();


        // Initialize debug info
        debugInfo = "Starting search with FEN " + FenUtility.CurrentFen(board);
        abortSearch = false;
        searchTotalTimer = Stopwatch.StartNew();



        // Start Lazy SMP
        StartMultiThreadedSearch();

        Move m = threadWorkerDatas[0].bestMove;

        if (m.IsNull)
        {
            m = threadWorkerDatas[0].moveGenerator.GenerateMoves(board)[0];
        }
        OnSearchComplete?.Invoke(m);
        abortSearch = false;
    }


    void StartMultiThreadedSearch()
    {
        List<Task> tasks = new List<Task>();


        for (int i = 1; i < threadNumber; i++)
        {
            int threadIndex = i;
            Task task = Task.Factory.StartNew(() => RunIterativeDeepeningSearch(threadIndex), TaskCreationOptions.LongRunning);
            tasks.Add(task);
        }
        RunIterativeDeepeningSearch(0);

        Task.WhenAll(tasks).Wait();
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

    void RunIterativeDeepeningSearch(int thread)
    {
        int a = negativeInfinity;
        int b = positiveInfinity;
        int alphaAspirationWindowsFailed = 0;
        int betaAspirationWindowsFailed = 0;
        threadWorkerDatas[thread].searchIterationTimer.Start();
        for (int searchDepth = 1; searchDepth <= 220; searchDepth++)
        {
            hasSearchedAtLeastOneMove = false;
            debugInfo += "\nStarting Iteration: " + searchDepth;
            currentIterationDepth = searchDepth;

            threadWorkerDatas[thread].lastIterationEval = SearchMoves(thread,searchDepth, 0, a, b);
            if (abortSearch)
            {
                if (hasSearchedAtLeastOneMove)
                {
                    threadWorkerDatas[thread].bestMove = threadWorkerDatas[thread].bestMoveThisIteration;
                    threadWorkerDatas[thread].bestEval = threadWorkerDatas[thread].bestEvalThisIteration;
                    threadWorkerDatas[thread].searchDiagnostics.move = MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].bestMove);
                    threadWorkerDatas[thread].searchDiagnostics.eval = threadWorkerDatas[thread].bestEval;
                    threadWorkerDatas[thread].searchDiagnostics.moveIsFromPartialSearch = true;
                    debugInfo += "\nUsing partial search result: " + MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].bestMove) + " Eval: " + threadWorkerDatas[thread].bestEval;

                }
                debugInfo += "\nSearch aborted";
                break;
            }
            else
            {
                age++;

                int alphaIncrement;
                int betaIncrement;
                if (threadWorkerDatas[thread].lastIterationEval <= a || threadWorkerDatas[thread].lastIterationEval >= b)
                {
                    if (threadWorkerDatas[thread].lastIterationEval <= a)
                    {
                        alphaAspirationWindowsFailed++;
                    }
                    else
                    {
                        betaAspirationWindowsFailed++;
                    }
                    alphaIncrement = (int)Pow(aspirationWindows, (alphaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                    betaIncrement = (int)Pow(aspirationWindows, (betaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                    a = threadWorkerDatas[thread].lastIterationEval - alphaIncrement;
                    b = threadWorkerDatas[thread].lastIterationEval + betaIncrement;
                    searchDepth--;

                    continue;
                }

                alphaAspirationWindowsFailed = 0;
                betaAspirationWindowsFailed = 0;
                alphaIncrement = (int)Pow(aspirationWindows, (alphaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                betaIncrement = (int)Pow(aspirationWindows, (betaAspirationWindowsFailed + 1) * aspirationWindowsPowerMultiplicator);
                a = threadWorkerDatas[thread].bestEvalThisIteration - alphaIncrement;
                b = threadWorkerDatas[thread].bestEvalThisIteration + betaIncrement;

                threadWorkerDatas[thread].bestMove = threadWorkerDatas[thread].bestMoveThisIteration;
                threadWorkerDatas[thread].bestEval = threadWorkerDatas[thread].bestEvalThisIteration;
                threadWorkerDatas[thread].currentDepth = searchDepth;
                

                // Only display the info of the first thread
                if (thread == 0)
                {
                    string pvLineName = "";
                    for (int count = 0; count < threadWorkerDatas[thread].pvLength[0]; count++)
                    {
                        pvLineName += MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].pvTable[0, count]).Replace("=", "") + " ";
                    }


                    var timeElapsedInIteration = threadWorkerDatas[thread].searchIterationTimer.Elapsed;
                    var totalElapsedTime = searchTotalTimer.Elapsed;
                    float totalElapsedTimeSecond = totalElapsedTime.Seconds + totalElapsedTime.Milliseconds / 1000f;
                    float nps = threadWorkerDatas[thread].searchDiagnostics.numNodes / totalElapsedTimeSecond;
                    if (nps > Int32.MaxValue || nps != nps)
                    {
                        nps = 0f;
                    }
                    int nodesPerSecond = Convert.ToInt32(nps);
                    Console.WriteLine($"info depth {threadWorkerDatas[thread].currentDepth} score cp {threadWorkerDatas[thread].bestEval} nodes {threadWorkerDatas[thread].searchDiagnostics.numNodes} nps {nodesPerSecond} time {timeElapsedInIteration.Milliseconds + timeElapsedInIteration.Seconds * 1000} pv {pvLineName} tthits {threadWorkerDatas[thread].searchDiagnostics.tthit}");
                    // Update diagnostics
                    debugInfo += "\nIteration result: " + MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].bestMove) + " Eval: " + threadWorkerDatas[thread].bestEval;
                    if (IsMateScore(threadWorkerDatas[thread].bestEval))
                    {
                        debugInfo += " Mate in ply: " + NumPlyToMateFromScore(threadWorkerDatas[thread].bestEval);
                    }
                }
                threadWorkerDatas[thread].searchIterationTimer.Restart();


                threadWorkerDatas[thread].bestEvalThisIteration = int.MinValue;
                threadWorkerDatas[thread].bestMoveThisIteration = Move.NullMove;

                // Update diagnostics
                threadWorkerDatas[thread].searchDiagnostics.numCompletedIterations = searchDepth;
                threadWorkerDatas[thread].searchDiagnostics.move = MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].bestMove);
                threadWorkerDatas[thread].searchDiagnostics.eval = threadWorkerDatas[thread].bestEval;
                // Exit search if found a mate within search depth.
                // A mate found outside of search depth (due to extensions) may not be the fastest mate.
                if (IsMateScore(threadWorkerDatas[thread].bestEval) && NumPlyToMateFromScore(threadWorkerDatas[thread].bestEval) <= searchDepth)
                {
                    debugInfo += "\nExitting search due to mate found within search depth";
                    break;
                }
            }
        }
    }

    

    // Main search function
    int SearchMoves(int threadIndex,int depth, int plyFromRoot, int alpha, int beta, int numExtensions = 0, Move prevMove = default, bool prevWasCapture = false, bool doNull = true, bool isPvNode = false)
    {
        if (abortSearch)
        {
            return 0;
        }

        // init PV length
        threadWorkerDatas[threadIndex].pvLength[plyFromRoot] = plyFromRoot;

        // manage draw and checkmate
        if (plyFromRoot > 0)
        {
            // detect draw by repetition
            if (threadWorkerDatas[threadIndex].board.CurrentGameState.fiftyMoveCounter >= 100 || threadWorkerDatas[threadIndex].repetitionTable.Contains(threadWorkerDatas[threadIndex].board.CurrentGameState.zobristKey))
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

        if (depth <= 0)
        {
            return QuiescenceSearch(threadIndex, alpha, beta, plyFromRoot);
        }

        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(threadWorkerDatas[threadIndex].board, depth, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed)
        {
            if (plyFromRoot == 0)
            {
                threadWorkerDatas[threadIndex].bestMoveThisIteration = tTable.GetStoredMove(threadWorkerDatas[threadIndex].board);
                threadWorkerDatas[threadIndex].bestEvalThisIteration = tTable.GetStoredScore(threadWorkerDatas[threadIndex].board);
                //Debug.Log ("move retrieved " + bestMoveThisIteration.Name + " Node type: " + tt.entries[tt.Index].nodeType + " depth: " + tt.entries[tt.Index].depth);
            }



            threadWorkerDatas[threadIndex].searchDiagnostics.tthit++;
            return ttVal;
        }

        
        // Null move prunning
        if (depth >= 1 && !threadWorkerDatas[threadIndex].board.IsInCheck() && plyFromRoot > 0 && doNull && !isPvNode)
        {
            threadWorkerDatas[threadIndex].board.MakeNullMove();
            int R = depth > 6 ? maxNullMoveR : minNullMoveR;
            int evaluation = -SearchMoves(threadIndex, depth - R - 1, plyFromRoot + 1, -beta, -beta + 1, doNull:false);
            threadWorkerDatas[threadIndex].board.UnmakeNullMove();

            if (abortSearch)
            {
                return 0;
            }
            if (evaluation >= beta)
            {
                depth -= nullMoveDepthReduction;
                if (depth <= 0)
                {
                    return QuiescenceSearch(threadIndex, alpha, beta, plyFromRoot);
                }
            }
        }
        


        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        threadWorkerDatas[threadIndex].moveGenerator.GenerateMoves(threadWorkerDatas[threadIndex].board, ref moves, capturesOnly: false);
        Move prevBestMove = plyFromRoot == 0 ? threadWorkerDatas[threadIndex].bestMove : tTable.GetStoredMove(threadWorkerDatas[threadIndex].board);
        threadWorkerDatas[threadIndex].moveOrdering.OrderMoves(prevBestMove, threadWorkerDatas[threadIndex].board, moves, threadWorkerDatas[0].moveGenerator.opponentAttackMap, threadWorkerDatas[0].moveGenerator.opponentPawnAttackMap, false, plyFromRoot);
        if (moves.Length == 0)
        {
            if (threadWorkerDatas[0].moveGenerator.InCheck())
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
            bool wasPawnMove = Piece.PieceType(threadWorkerDatas[threadIndex].board.Square[prevMove.TargetSquare]) == Piece.Pawn;
            threadWorkerDatas[threadIndex].repetitionTable.Push(threadWorkerDatas[threadIndex].board.CurrentGameState.zobristKey, prevWasCapture || wasPawnMove);
        }

        int evalType = TranspositionTable.UpperBound;
        Move bestMoveInThisPosition = Move.NullMove;
        bool isPvChild = isPvNode;
        isPvNode = Move.SameMove(threadWorkerDatas[threadIndex].pvTable[0, plyFromRoot], moves[0]) && (isPvChild || plyFromRoot == 0);


        for (int i = 0; i < moves.Length; i++)
        {
            int capturedPieceType = Piece.PieceType(threadWorkerDatas[threadIndex].board.Square[moves[i].TargetSquare]);
            bool isCapture = capturedPieceType != Piece.None;
            threadWorkerDatas[threadIndex].board.MakeMove(moves[i], true);

            // Move extension : search interesting board to a deeper depth
            int extension = 0;
            if (numExtensions < maxExtentions)
            {
                int movedPieceType = Piece.PieceType(threadWorkerDatas[threadIndex].board.Square[moves[i].TargetSquare]);
                int targetRank = BoardHelper.RankIndex(moves[i].TargetSquare);
                if (threadWorkerDatas[threadIndex].board.IsInCheck())
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
            if (i == 0 && isPvNode)
            {
                eval = -SearchMoves(threadIndex, depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture, isPvNode:true);
                isPvNode = false;
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
                eval = -SearchMoves(threadIndex, depth - 1 - reduction, plyFromRoot + 1, -alpha - 1, -alpha, numExtensions, moves[i], isCapture);
                if (eval > alpha)
                {
                    eval = -SearchMoves(threadIndex, depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture);
                }
            }

            threadWorkerDatas[threadIndex].board.UnmakeMove(moves[i], true);

            if (abortSearch)
            {
                return 0;
            }
            threadWorkerDatas[threadIndex].searchDiagnostics.numNodes++;

            if (eval >= beta)
            {
                tTable.StoreEvaluation(threadWorkerDatas[threadIndex].board, depth, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i], age);

                // Update killer moves and history heuristic (note: don't include captures as theres are ranked highly anyway)
                if (!isCapture)
                {
                    if (plyFromRoot < MoveOrdering.maxKillerMovePly)
                    {
                        threadWorkerDatas[threadIndex].moveOrdering.killerMoves[plyFromRoot].Add(moves[i]);
                    }
                    int historyScore = depth * depth;
                    threadWorkerDatas[threadIndex].moveOrdering.History[threadWorkerDatas[threadIndex].board.MoveColourIndex, moves[i].StartSquare, moves[i].TargetSquare] += historyScore;
                }
                if (plyFromRoot > 0)
                {
                    threadWorkerDatas[threadIndex].repetitionTable.TryPop();
                }

                threadWorkerDatas[threadIndex].searchDiagnostics.numCutOffs++;
                return beta;
            }


            if (eval > alpha)
            {
                evalType = TranspositionTable.Exact;
                bestMoveInThisPosition = moves[i];

                alpha = eval;

                // write PV move
                threadWorkerDatas[threadIndex].pvTable[plyFromRoot, plyFromRoot] = bestMoveInThisPosition;

                // loop over the next ply
                for (int nextPly = plyFromRoot + 1; nextPly < threadWorkerDatas[threadIndex].pvLength[plyFromRoot + 1]; nextPly++)
                {
                    // copy move from deeper ply into current ply's line
                    threadWorkerDatas[threadIndex].pvTable[plyFromRoot, nextPly] = threadWorkerDatas[threadIndex].pvTable[plyFromRoot + 1, nextPly];
                }
                threadWorkerDatas[threadIndex].pvLength[plyFromRoot] = threadWorkerDatas[threadIndex].pvLength[plyFromRoot + 1];

                if (plyFromRoot == 0)
                {
                    threadWorkerDatas[threadIndex].bestMoveThisIteration = moves[i];
                    threadWorkerDatas[threadIndex].bestEvalThisIteration = eval;

                }
            }
        }

        if (plyFromRoot > 0)
        {
            threadWorkerDatas[threadIndex].repetitionTable.TryPop();
        }


        tTable.StoreEvaluation(threadWorkerDatas[threadIndex].board, depth, plyFromRoot, alpha, evalType, bestMoveInThisPosition, age);

        return alpha;
    }

    // Search to a further depth until a stable position is reached (stable means no capture left)
    int QuiescenceSearch(int threadIndex, int alpha, int beta, int plyFromRoot)
    {
        if (abortSearch)
        {
            return 0;
        }

        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(threadWorkerDatas[threadIndex].board, 0, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed)
        {
            threadWorkerDatas[threadIndex].searchDiagnostics.tthit++;
            return ttVal;
        }


        int eval = threadWorkerDatas[threadIndex].evaluation.Evaluate(threadWorkerDatas[threadIndex].board, allowNNUE);
        threadWorkerDatas[threadIndex].searchDiagnostics.numPositionsEvaluated++;
        if (eval >= beta)
        {
            threadWorkerDatas[threadIndex].searchDiagnostics.numCutOffs++;
            return beta;
        }
        if (eval > alpha)
        {
            alpha = eval;
        }

        Span<Move> moves = stackalloc Move[128];
        int evalType = TranspositionTable.UpperBound;
        threadWorkerDatas[threadIndex].moveGenerator.GenerateMoves(threadWorkerDatas[threadIndex].board, ref moves, capturesOnly: true);
        threadWorkerDatas[threadIndex].moveOrdering.OrderMoves(Move.NullMove, threadWorkerDatas[threadIndex].board, moves, threadWorkerDatas[0].moveGenerator.opponentAttackMap, threadWorkerDatas[threadIndex].moveGenerator.opponentPawnAttackMap, true, 0);
        for (int i = 0;i < moves.Length;i++)
        {
            threadWorkerDatas[threadIndex].board.MakeMove(moves[i], true);
            eval = -QuiescenceSearch(threadIndex, -beta, -alpha, plyFromRoot + 1);
            threadWorkerDatas[threadIndex].board.UnmakeMove(moves[i], true);

            threadWorkerDatas[threadIndex].searchDiagnostics.numNodes++;

            if (eval >= beta)
            {
                tTable.StoreEvaluation(threadWorkerDatas[threadIndex].board,0, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i], age);
                threadWorkerDatas[threadIndex].searchDiagnostics.numCutOffs++;
                return beta;
            }
            if (eval > alpha)
            {
                evalType = TranspositionTable.Exact;
                alpha = eval;
            }
        }
        tTable.StoreEvaluation(threadWorkerDatas[threadIndex].board, 0, plyFromRoot, alpha, evalType, Move.NullMove, age);

        return alpha;
    }


    public static int NumPlyToMateFromScore(int score)
    {
        return immediateMateScore - Abs(score);

    }

    public void ClearForNewPosition()
    {
        tTable.Clear();
        for (int i = 0; i < threadWorkerDatas.Length; i++)
        {
            threadWorkerDatas[i].moveOrdering.ClearKillers();
        }

        age = 0;
    }

    void InitWorkersDatas(int workerNumber)
    {
        int workers = Min(workerNumber, maxThreads);
        threadWorkerDatas = new ThreadWorkerData[workers];

        for (int i = 0; i < workers; i++)
        {
            threadWorkerDatas[i] = new ThreadWorkerData(board, tTable);
        }
    }

    void UpdateWorkersDatas()
    {
        for (int i = 0;i < threadWorkerDatas.Length; i++)
        {
            threadWorkerDatas[i].board = new Board();
            threadWorkerDatas[i].board.LoadPosition(board.CurrentFEN);
            threadWorkerDatas[i].currentDepth = 0;
            threadWorkerDatas[i].searchDiagnostics = new SearchDiagnostics();
            threadWorkerDatas[i].searchIterationTimer = new Stopwatch();
            
        }
    }

    bool IsMainThread(int thread, int[] currentDepthList)
    {
        for (int i = 0; i < threadWorkerDatas.Length; i++)
        {
            currentDepthList[i] = threadWorkerDatas[i].currentDepth;
        }
        int max = int.MinValue;
        for (int i = 0; i < currentDepthList.Length; i++)
        {
            if (i == thread)
                continue;

            if (currentDepthList[i] > max)
                max = currentDepthList[i];
        }
        return threadWorkerDatas[thread].currentDepth > max;
    }

    public struct ThreadWorkerData
    {
        public Board board;
        public RepetitionTable repetitionTable;
        public MoveGenerator moveGenerator;
        public Evaluation evaluation;
        public MoveOrdering moveOrdering;
        public int currentDepth;
        public int bestEvalThisIteration;
        public Move bestMoveThisIteration;
        public int lastIterationEval;
        public int bestEval;
        public Move bestMove;
        public int[] pvLength;
        public Move[,] pvTable;
        public SearchDiagnostics searchDiagnostics;
        public Stopwatch searchIterationTimer;

        public ThreadWorkerData(Board board, TranspositionTable transpositionTable)
        {
            this.board = new Board();
            this.board.LoadPosition(board.CurrentFEN);
            repetitionTable = new RepetitionTable();
            moveGenerator = new MoveGenerator();
            moveGenerator.promotionsToGenerate = MoveGenerator.PromotionMode.All;
            evaluation = new Evaluation();
            moveOrdering = new MoveOrdering(moveGenerator, transpositionTable);
            currentDepth = 0;
            bestEvalThisIteration = int.MinValue;
            bestMoveThisIteration = Move.NullMove;
            lastIterationEval = 0;
            bestEval = 0;
            bestMove = Move.NullMove;
            pvLength = new int[272];
            pvTable = new Move[272, 272];
            searchDiagnostics = new SearchDiagnostics();
            searchIterationTimer = new Stopwatch();
        }
    }


    [Serializable]
    public struct SearchDiagnostics
    {
        public int numCompletedIterations;
        public int numPositionsEvaluated;
        public int numNodes;
        public int tthit;
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
