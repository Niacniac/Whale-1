using static System.Math;

public class Bot
{
    // # Settings
    const bool useOpeningBook = false;
    const int maxBookPly = 16;
    // Limit the amount of time the bot can spend per move (mainly for
    // games against human opponents, so not boring to play against).
    const bool useMaxThinkTime = false;
    const int maxThinkTimeMs = 2500;

    // Public stuff
    public event Action<string>? OnMoveChosen;
    public bool IsThinking { get; private set; }
    public bool LatestMoveIsBookMove { get; private set; }

    // References
    readonly Search searcher;
    readonly Board board;
    readonly Book book;
    readonly AutoResetEvent searchWaitHandle;
    CancellationTokenSource? cancelSearchTimer;

    // State
    int currentSearchID;
    bool isQuitting;

    public Bot()
    {
        board = Board.CreateBoard();
        searcher = new Search(board);
        searcher.OnSearchComplete += OnSearchComplete;

        book = new Book(Whale_1.Properties.Resources.Book);
        searchWaitHandle = new(false);

        Task.Factory.StartNew(SearchThread, TaskCreationOptions.LongRunning);
    }

    public void NotifyNewGame()
    {
        searcher.ClearForNewPosition();
    }

    public void SetPosition(string fen)
    {
        board.LoadPosition(fen);
    }

    public void SetOption(int optionNum, int value)
    {
        switch (optionNum)
        {
            case 0:
                searcher.ResizeTranspositionTable((ulong)value);
                searcher.TranspositionTableSize = (uint)value;
                break;
            case 1:
                searcher.ThreadNumber = value;
                searcher.InitWorkersDatas();
                break;
            case 2:
                searcher.AllowNNUE = Convert.ToBoolean(value);
                break;
        }
    }
    public void MakeMove(string moveString)
    {
        Move move = MoveUtility.GetMoveFromUCIName(moveString, board);
        board.MakeMove(move);
    }

    public int ChooseThinkTime(int timeRemainingWhiteMs, int timeRemainingBlackMs, int incrementWhiteMs, int incrementBlackMs)
    {
        int myTimeRemainingMs = board.IsWhiteToMove ? timeRemainingWhiteMs : timeRemainingBlackMs;
        int myIncrementMs = board.IsWhiteToMove ? incrementWhiteMs : incrementBlackMs;
        // Get a fraction of remaining time to use for current move
        double thinkTimeMs = myTimeRemainingMs / 40.0;
        // Clamp think time if a maximum limit is imposed
        if (useMaxThinkTime)
        {
            thinkTimeMs = Min(maxThinkTimeMs, thinkTimeMs);
        }
        // Add increment
        if (myTimeRemainingMs > myIncrementMs * 2)
        {
            thinkTimeMs += myIncrementMs * 0.8;
        }

        double minThinkTime = Min(50, myTimeRemainingMs * 0.25);
        return (int)Ceiling(Max(minThinkTime, thinkTimeMs));
    }

    public void ThinkTimed(int timeMs, bool NoTime = false)
    {
        LatestMoveIsBookMove = false;
        IsThinking = true;
        cancelSearchTimer?.Cancel();

        if (TryGetOpeningBookMove(out Move bookMove))
        {
            LatestMoveIsBookMove = true;
            OnSearchComplete(bookMove);
        }
        else
        {
            StartSearch(timeMs, NoTime);
        }
    }

    void StartSearch(int timeMs, bool infiniteSearch = false)
    {
        currentSearchID++;
        searchWaitHandle.Set();
        if (!infiniteSearch)
        {
            cancelSearchTimer = new CancellationTokenSource();
            Task.Delay(timeMs, cancelSearchTimer.Token).ContinueWith((t) => EndSearch(currentSearchID));
        }
    }

    void SearchThread()
    {
        while (!isQuitting)
        {
            searchWaitHandle.WaitOne();
            searcher.StartSearch();
        }
    }

    public void StopThinking()
    {
        EndSearch();
    }

    public void Quit()
    {
        isQuitting = true;
        EndSearch();
    }

    public string GetBoardDiagram() => board.ToString();

    void EndSearch()
    {
        cancelSearchTimer?.Cancel();
        if (IsThinking)
        {
            searcher.EndSearch();
        }
    }

    void EndSearch(int searchID)
    {
        // If search timer has been cancelled, the search will have been stopped already
        if (cancelSearchTimer != null && cancelSearchTimer.IsCancellationRequested)
        {
            return;
        }

        if (currentSearchID == searchID)
        {
            EndSearch();
        }
    }

    void OnSearchComplete(Move move)
    {
        IsThinking = false;

        string moveName = MoveUtility.GetMoveNameUCI(move).Replace("=", "");

        OnMoveChosen?.Invoke(moveName);
    }

    bool TryGetOpeningBookMove(out Move bookMove)
    {
        if (useOpeningBook && board.PlyCount <= maxBookPly && book.TryGetBookMove(FenUtility.CurrentFen(board), out string moveString))
        {
            bookMove = MoveUtility.GetMoveFromUCIName(moveString, board);
            return true;
        }
        bookMove = Move.NullMove;
        return false;
    }

    public static string GetResourcePath(params string[] localPath)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "resources", Path.Combine(localPath));
    }

    public static string ReadResourceFile(string localPath)
    {
        return File.ReadAllText(GetResourcePath(localPath));
    }
}
