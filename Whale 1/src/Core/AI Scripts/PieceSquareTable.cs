
public static class PieceSquareTable
{
    public static int Read(int[] table, int square, bool isWhite)
    {
        if (isWhite)
        {
            int file = BoardHelper.FileIndex(square);
            int rank = BoardHelper.RankIndex(square);
            rank = 7 - rank;
            square = BoardHelper.IndexFromCoord(file, rank);
        }

        return table[square];
    }

    public static int Read(int piece, int square)
    {
        return Tables[piece][square];
    }

    public static readonly int[] pawns = {
            0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
            5,  5, 10, 25, 25, 10,  5,  5,
            0,  0,  0, 20, 20,  0,  0,  0,
            5, -5,-10,  0,  0,-10, -5,  5,
            5, 10, 10,-20,-20, 10, 10,  5,
            0,  0,  0,  0,  0,  0,  0,  0
    };
    public static readonly int[] PawnsEnd = {
             0,   0,   0,   0,   0,   0,   0,   0,
            80,  80,  80,  80,  80,  80,  80,  80,
            50,  50,  50,  50,  50,  50,  50,  50,
            30,  30,  30,  30,  30,  30,  30,  30,
            20,  20,  20,  20,  20,  20,  20,  20,
            10,  10,  10,  10,  10,  10,  10,  10,
            10,  10,  10,  10,  10,  10,  10,  10,
             0,   0,   0,   0,   0,   0,   0,   0
        };

    public static readonly int[] knights = {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    public static readonly int[] bishops = {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
    };
    public static readonly int[] rooks = {
            0,  0,  0,  0,  0,  0,  0,  0,
            5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            0,  0,  0,  5,  5,  0,  0,  0
    };
    public static readonly int[] queens = {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
            -5,  0,  5,  5,  5,  5,  0, -5,
            0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
    };
    public static readonly int[] KingStart = {
        -80, -70, -70, -70, -70, -70, -70, -80,
        -60, -60, -60, -60, -60, -60, -60, -60,
        -40, -50, -50, -60, -60, -50, -50, -40,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -20, -30, -30, -40, -40, -30, -30, -20,
        -10, -20, -20, -20, -20, -20, -20, -10,
         20,  20,  -5,  -5,  -5,  -5,  20,  20,
         20,  30,  10,   0,   0,  10,  30,  20 
    };
    public static readonly int[] KingEnd = { 
        -20, -10, -10, -10, -10, -10, -10, -20,
         -5,   0,   5,   5,   5,   5,   0,  -5,
        -10,  -5,  20,  30,  30,  20,  -5, -10,
        -15, -10,  35,  45,  45,  35, -10, -15,
        -20, -15,  30,  40,  40,  30, -15, -20,
        -25, -20,  20,  25,  25,  20, -20, -25,
        -30, -25,   0,   0,   0,   0, -25, -30,
        -50, -30, -30, -30, -30, -30, -30, -50 
    };
    public static readonly int[][] Tables;

    static PieceSquareTable()
    {
        Tables = new int[Piece.MaxPieceIndex + 1][];
        Tables[Piece.MakePiece(Piece.Pawn, Piece.White)] = pawns;
        Tables[Piece.MakePiece(Piece.Rook, Piece.White)] = rooks;
        Tables[Piece.MakePiece(Piece.Knight, Piece.White)] = knights;
        Tables[Piece.MakePiece(Piece.Bishop, Piece.White)] = bishops;
        Tables[Piece.MakePiece(Piece.Queen, Piece.White)] = queens;

        Tables[Piece.MakePiece(Piece.Pawn, Piece.Black)] = GetFlippedTable(pawns);
        Tables[Piece.MakePiece(Piece.Rook, Piece.Black)] = GetFlippedTable(rooks);
        Tables[Piece.MakePiece(Piece.Knight, Piece.Black)] = GetFlippedTable(knights);
        Tables[Piece.MakePiece(Piece.Bishop, Piece.Black)] = GetFlippedTable(bishops);
        Tables[Piece.MakePiece(Piece.Queen, Piece.Black)] = GetFlippedTable(queens);
    }

    static int[] GetFlippedTable(int[] table)
    {
        int[] flippedTable = new int[table.Length];

        for (int i = 0; i < table.Length; i++)
        {
            Coord coord = new Coord(i);
            Coord flippedCoord = new Coord(coord.fileIndex, 7 - coord.rankIndex);
            flippedTable[flippedCoord.SquareIndex] = table[i];
        }
        return flippedTable;
    }
}

