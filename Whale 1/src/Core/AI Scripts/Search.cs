using static System.Math;
using System.Diagnostics;
using Whale_1.src.Core.AI_Scripts;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;


public class Search
{
    // Constants
    const int maxThreads = 128;
    const int maxExtentions = 16;
    const double aspirationWindowExponent = 3.5d;
    const int aspirationWindowBase = 15;
    const int immediateMateScore = 100000;
    const int positiveInfinity = 9999999;
    const int negativeInfinity = -positiveInfinity;

    const int maxNullMoveR = 4;
    const int minNullMoveR = 4;
    const int nullMoveDepthReduction = 4;
    const int futilityMargin = 350; // value of a piece
    public event Action<Move>? OnSearchComplete;

    bool hasSearchedAtLeastOneMove;
    bool abortSearch;

    // References
    Board board;
    TranspositionTable tTable;
    int currentIterativeSearchDepth;
    uint age;
    int[] reductions = new int[220];
    ThreadWorkerData[] threadWorkerDatas;

    // Options
    private int _threadNumber = 1;
    private ulong _transpositionTableSize = 16;
    private bool _allowNNUE = Avx2.IsSupported;

    public int ThreadNumber
    {
        get { return _threadNumber; }
        set { _threadNumber = Math.Clamp(value, 1, maxThreads); }
    }
    public ulong TranspositionTableSize
    {
        get { return _transpositionTableSize; }
        set { _transpositionTableSize = Math.Clamp(value, 16, 32000); }
    }
    public bool AllowNNUE
    {
        get { return _allowNNUE; }
        set { _allowNNUE = Avx2.IsSupported && value; }
    }


    // Diagnostics
    int currentIterationDepth;
    Stopwatch searchTotalTimer;
    public string debugInfo;
    

    public Search(Board board)
    {
        this.board = board;
        tTable = new TranspositionTable(TranspositionTableSize);
        InitWorkersDatas(ThreadNumber);
        InitTables(ThreadNumber);
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


        for (int i = 1; i < ThreadNumber; i++)
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
        int lastInBoundEval = 0;
        if (thread == 0) { age++; }

        for (int searchDepth = 1; searchDepth <= 220; searchDepth++)
        {
            threadWorkerDatas[thread].searchIterationTimer.Start();
            hasSearchedAtLeastOneMove = false;
            debugInfo += "\nStarting Iteration: " + searchDepth;
            currentIterationDepth = searchDepth;
            threadWorkerDatas[thread].rootDelta = b - a;

            threadWorkerDatas[thread].lastIterationEval = SearchMoves(thread,searchDepth, 0, a, b, isPvNode:true);
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

                int alphaIncrement;
                int betaIncrement;
                // If the eval is outside the aspiration windows we have to research with a bigger window
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

                    alphaIncrement = aspirationWindowBase * (int)Pow(aspirationWindowExponent, alphaAspirationWindowsFailed);
                    betaIncrement = aspirationWindowBase * (int)Pow(aspirationWindowExponent, betaAspirationWindowsFailed);
                    a = Clamp(lastInBoundEval - alphaIncrement, int.MinValue, lastInBoundEval);
                    b = Clamp(lastInBoundEval + betaIncrement, lastInBoundEval, int.MaxValue);
                    searchDepth--;

                    continue;
                }

                // The eval is in the bound of the aspiration window
                alphaAspirationWindowsFailed = 0;
                betaAspirationWindowsFailed = 0;
                alphaIncrement = aspirationWindowBase * (int)Pow(aspirationWindowExponent, alphaAspirationWindowsFailed);
                betaIncrement = aspirationWindowBase * (int)Pow(aspirationWindowExponent, betaAspirationWindowsFailed);
                a = threadWorkerDatas[thread].bestEvalThisIteration - alphaIncrement;
                b = threadWorkerDatas[thread].bestEvalThisIteration + betaIncrement;
                lastInBoundEval = threadWorkerDatas[thread].bestEvalThisIteration;

                threadWorkerDatas[thread].bestMove = threadWorkerDatas[thread].bestMoveThisIteration;
                threadWorkerDatas[thread].bestEval = threadWorkerDatas[thread].bestEvalThisIteration;
                threadWorkerDatas[thread].currentDepth = searchDepth;
                

                // Only display the info of the first thread
                if (thread == 0)
                {
                    threadWorkerDatas[thread].searchIterationTimer.Stop();
                    string pvLineName = "";
                    for (int count = 0; count < threadWorkerDatas[thread].pvLength[0]; count++)
                    {
                        pvLineName += MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].pvTable[0, count]).Replace("=", "") + " ";
                    }

                    float perMillTTfull = Convert.ToSingle(tTable.entriesNum) / Convert.ToSingle(tTable.count) * 1000f;

                    var totalElapsedTime = searchTotalTimer.Elapsed;
                    int totalNodes = GetTotalNodes();
                    float totalElapsedTimeSecond = totalElapsedTime.Seconds + totalElapsedTime.Milliseconds / 1000f + totalElapsedTime.Minutes * 60f;
                    float nps = totalNodes / totalElapsedTimeSecond;
                    threadWorkerDatas[thread].searchIterationTimer.Reset();
                    if (nps > Int32.MaxValue || nps != nps)
                    {
                        nps = 0f;
                    }
                    int nodesPerSecond = Convert.ToInt32(nps);
                    Console.WriteLine($"info depth {threadWorkerDatas[thread].currentDepth} score cp {threadWorkerDatas[thread].bestEval} nodes {totalNodes} nps {nodesPerSecond} hashfull {Convert.ToInt32(perMillTTfull)} time {totalElapsedTime.Milliseconds + totalElapsedTime.Seconds * 1000 + totalElapsedTime.Minutes * 60 * 1000} pv {pvLineName}");
                    // Update diagnostics
                    debugInfo += "\nIteration result: " + MoveUtility.GetMoveNameUCI(threadWorkerDatas[thread].bestMove) + " Eval: " + threadWorkerDatas[thread].bestEval;
                    if (IsMateScore(threadWorkerDatas[thread].bestEval))
                    {
                        debugInfo += " Mate in ply: " + NumPlyToMateFromScore(threadWorkerDatas[thread].bestEval);
                    }
                }
                


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

        bool isRootNode = plyFromRoot == 0;
        threadWorkerDatas[threadIndex].searchDiagnostics.numNodes++;

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

        // When the depths get to 0 or bellow (in case of reduction) we call the quiescence search
        if (depth <= 0)
        {
            return QuiescenceSearch(threadIndex, alpha, beta, plyFromRoot);
        }

        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(threadWorkerDatas[threadIndex].board, depth, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed && !isPvNode)
        {
            if (plyFromRoot == 0)
            {
                threadWorkerDatas[threadIndex].bestMoveThisIteration = tTable.GetStoredMove(threadWorkerDatas[threadIndex].board);
                threadWorkerDatas[threadIndex].bestEvalThisIteration = tTable.GetStoredScore(threadWorkerDatas[threadIndex].board);             
            }

            threadWorkerDatas[threadIndex].searchDiagnostics.tthit++;
            return ttVal;
        }

        
        // Null move prunning with verification search to avoid zugswang to false the evaluation
        if (depth >= 1 && !threadWorkerDatas[threadIndex].board.IsInCheck() && plyFromRoot > 0 && doNull && !isPvNode && plyFromRoot >= threadWorkerDatas[threadIndex].nmpMinPly)
        {
            threadWorkerDatas[threadIndex].board.MakeNullMove();
            int R = depth / 3 + 4;
            int nullEvaluation = -SearchMoves(threadIndex, depth - R, plyFromRoot + 1, -beta, -beta + 1, doNull:false);
            threadWorkerDatas[threadIndex].board.UnmakeNullMove();

            if (abortSearch)
            {
                return 0;
            }

            if (nullEvaluation >= beta)
            {
                if (depth < 10 || threadWorkerDatas[threadIndex].nmpMinPly != 0)
                {
                    return nullEvaluation;
                }

                threadWorkerDatas[threadIndex].nmpMinPly = plyFromRoot + 3 * (depth - R) / 4;

                int v = SearchMoves(threadIndex, depth - R, plyFromRoot + 1, beta - 1, beta);

                threadWorkerDatas[threadIndex].nmpMinPly = 0;

                if (v >= beta)
                {
                    return nullEvaluation;
                }

            }
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        threadWorkerDatas[threadIndex].moveGenerator.GenerateMoves(threadWorkerDatas[threadIndex].board, ref moves, capturesOnly: false);
        Move prevBestMove = plyFromRoot == 0 ? threadWorkerDatas[threadIndex].bestMove : tTable.GetStoredMove(threadWorkerDatas[threadIndex].board);
        threadWorkerDatas[threadIndex].moveOrdering.OrderMoves(prevBestMove, threadWorkerDatas[threadIndex].board, moves, threadWorkerDatas[threadIndex].moveGenerator.opponentAttackMap, threadWorkerDatas[threadIndex].moveGenerator.opponentPawnAttackMap, false, plyFromRoot, isRootNode, threadIndex);
        if (moves.Length == 0)
        {
            if (threadWorkerDatas[threadIndex].moveGenerator.InCheck())
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
        int staticEval = 0;

        // we only do the static evaluation of the position if depth == 1 && !isPvNode since we only use it for the futility move prunning at the moment 
        if (depth == 1 && !isPvNode)
        {
            staticEval = threadWorkerDatas[threadIndex].evaluation.Evaluate(threadWorkerDatas[threadIndex].board, AllowNNUE);
            threadWorkerDatas[threadIndex].searchDiagnostics.numPositionsEvaluated++;
        }

        // loop through all the legal move
        for (int i = 0; i < moves.Length; i++)
        {
            int capturedPieceType = Piece.PieceType(threadWorkerDatas[threadIndex].board.Square[moves[i].TargetSquare]);
            bool isCapture = capturedPieceType != Piece.None;



            
            // Futility prunning : we evaluate position at depth = 1 and if the evaluation + a margin is worst than alpha, we assume that the position can be skipped
            if (depth == 1 && !threadWorkerDatas[threadIndex].board.IsInCheck() && !isPvNode)
            {
                int futilMargin = futilityMargin;
                
                if (isCapture)
                {
                    futilMargin += Evaluation.pieceValueArray[capturedPieceType];
                }

                if ((staticEval + futilMargin) <= alpha)
                {
                    threadWorkerDatas[threadIndex].searchDiagnostics.numCutOffs++;
                    if (i == (moves.Length - 1))
                    {
                        if (plyFromRoot > 0)
                        {
                            threadWorkerDatas[threadIndex].repetitionTable.TryPop();
                        }
                        return alpha;
                    }
                    continue;
                }
            }

            // make the move
            MakeMove(threadIndex, moves[i]);

            // Move extension : search interesting moves to a deeper depth
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

            // Non-PV Search
            int eval = alpha - 1;
            if (i > 0 || !isPvNode) 
            { 

                // Late move reduction
                int reduction = 0;

                if (depth >= 3 && i > 0 && !isPvNode)
                {
                    reduction = CalculateReduction(depth, i + 1, beta - alpha, threadWorkerDatas[threadIndex].rootDelta);
                }

                eval = -SearchMoves(threadIndex, depth - 1 - reduction + extension, plyFromRoot + 1, -alpha - 1, -alpha, numExtensions + extension, moves[i], isCapture);
                if (eval > alpha && eval < beta)
                {
                    eval = -SearchMoves(threadIndex, depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture);
                }
            }

            // PV Search
            if (isPvNode && ( i == 0 || eval > alpha))
            {
                eval = -SearchMoves(threadIndex, depth - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, moves[i], isCapture, isPvNode: true);
            }

            UnmakeMove(threadIndex, moves[i]);

            if (abortSearch)
            {
                return 0;
            }

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

        threadWorkerDatas[threadIndex].searchDiagnostics.numNodes++;
        threadWorkerDatas[threadIndex].searchDiagnostics.numQNodes++;

        // Use the transposition table to see if the current position has already been reach
        int ttVal = tTable.LookupEvaluation(threadWorkerDatas[threadIndex].board, 0, plyFromRoot, alpha, beta);
        if (ttVal != TranspositionTable.LookupFailed)
        {
            threadWorkerDatas[threadIndex].searchDiagnostics.tthit++;
            return ttVal;
        }


        int eval = threadWorkerDatas[threadIndex].evaluation.Evaluate(threadWorkerDatas[threadIndex].board, AllowNNUE);
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
        threadWorkerDatas[threadIndex].moveOrdering.OrderMoves(tTable.GetStoredMove(threadWorkerDatas[threadIndex].board), threadWorkerDatas[threadIndex].board, moves, threadWorkerDatas[threadIndex].moveGenerator.opponentAttackMap, threadWorkerDatas[threadIndex].moveGenerator.opponentPawnAttackMap, true, 0, false, threadIndex);
        for (int i = 0;i < moves.Length;i++)
        {
            MakeMove(threadIndex, moves[i]);

            eval = -QuiescenceSearch(threadIndex, -beta, -alpha, plyFromRoot + 1);

            UnmakeMove(threadIndex, moves[i]);


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

    public void ResizeTranspositionTable(ulong sizeMB)
    {
        tTable.Resize(sizeMB);
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

    void InitTables(int workerNumber)
    {
        for (int i = 1; i < reductions.Length; ++i)
        {
            int value = (int)((20.37 + Math.Log(workerNumber) / 2) * Math.Log(i));
            reductions[i] = value;
        }
    }

    void MakeMove(int threadIndex, Move move)
    {
        if (AllowNNUE)
        {
            threadWorkerDatas[threadIndex].evaluation.nnue.UpdateAppenedFeatures(move, threadWorkerDatas[threadIndex].board, false);
            threadWorkerDatas[threadIndex].board.MakeMove(move, true);
        }
        else
        {
            threadWorkerDatas[threadIndex].board.MakeMove(move, true);
        }
    }

    void UnmakeMove(int threadIndex, Move move)
    {
        if (AllowNNUE)
        {
            threadWorkerDatas[threadIndex].board.UnmakeMove(move, true);
            threadWorkerDatas[threadIndex].evaluation.nnue.UpdateAppenedFeatures(move, threadWorkerDatas[threadIndex].board, true);
        }
        else
        {
            threadWorkerDatas[threadIndex].board.UnmakeMove(move, true);
        }

    }

    int CalculateReduction(int depth, int moveCount, int delta, int rootDelta)
    {
        int reductionScale = reductions[depth] * reductions[moveCount];
        return (reductionScale + 1177 - delta * 776 / rootDelta) / 1024;
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
            threadWorkerDatas[i].evaluation.nnue.TryUpdateAccumulators(threadWorkerDatas[i].board, true);
        }
    }


    int GetTotalNodes()
    {
        int totalNodes = 0;
        for (int i = 0; i < threadWorkerDatas.Length ; i++) 
        {
            totalNodes += threadWorkerDatas[i].searchDiagnostics.numNodes;
        }
        return totalNodes;
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
        public int nmpMinPly;
        public int rootDelta;
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
            nmpMinPly = 0;
            rootDelta = 0;
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
        public int numQNodes;
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
