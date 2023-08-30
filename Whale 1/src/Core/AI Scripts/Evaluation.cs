
public class Evaluation
{
    Board board;
    MoveGenerator moveGenerator;

    public EvaluationData whiteEval;
    public EvaluationData blackEval;
    // Value of each piece
    public const int pawnValue = 100;
    public const int knightValue = 300;
    public const int bishopValue = 310;
    public const int rookValue = 500;
    public const int queenValue = 900;

    static readonly int[] passedPawnBonuses = { 0, 120, 80, 50, 30, 15, 15 };
    static readonly int[] isolatedPawnPenaltyByCount = { 0, -10, -25, -50, -75, -75, -75, -75, -75 };

    const float endgameMaterialStart = rookValue * 2 + bishopValue + knightValue;

    static readonly int[] SafetyTable = {
          0,   0,   1,   2,   3,   5,   7,   9,  12,  15,
         18,  22,  26,  30,  35,  39,  44,  50,  56,  62,
         68,  75,  82,  85,  89,  97, 105, 113, 122, 131,
        140, 150, 169, 180, 191, 202, 213, 225, 237, 248,
        260, 272, 283, 295, 307, 319, 330, 342, 354, 366,
        377, 389, 401, 412, 424, 436, 448, 459, 471, 483,
        494, 500, 500, 500, 500, 500, 500, 500, 500, 500,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500
    };

    public Evaluation()
    {
        moveGenerator = new MoveGenerator();
    }

    public int Evaluate(Board board)
    {
        this.board = board;
        whiteEval = new EvaluationData();
        blackEval = new EvaluationData();

        MaterialInfo whiteMaterial = GetMaterialInfo(Board.WhiteIndex);
        MaterialInfo blackMaterial = GetMaterialInfo(Board.BlackIndex);

        whiteEval.materialScore = whiteMaterial.materialScore;
        blackEval.materialScore = blackMaterial.materialScore;

        whiteEval.pieceSquareScore = EvaluatePieceSquareTables(true, blackMaterial.endgameT);
        blackEval.pieceSquareScore = EvaluatePieceSquareTables(false, whiteMaterial.endgameT);

        whiteEval.mopUpScore = MopUpEval(true, whiteMaterial, blackMaterial);
        blackEval.mopUpScore = MopUpEval(false, blackMaterial, whiteMaterial);

        whiteEval.pawnScore = EvaluatePawns(Board.WhiteIndex);
        blackEval.pawnScore = EvaluatePawns(Board.BlackIndex);

        whiteEval.kingAttackScore = EvaluateKingAttack(true);
        blackEval.kingAttackScore = EvaluateKingAttack(false);

        int perspective = board.IsWhiteToMove? 1 : -1;
        // more than 0 good for white, less than 0 good for black
        int eval = whiteEval.Sum() - blackEval.Sum();
        return eval * perspective;
    }

    int CountMaterial(int colourIndex)
    {
        int material = 0;
        material += board.Pawns[colourIndex].Count * pawnValue;
        material += board.Knights[colourIndex].Count * knightValue;
        material += board.Bishops[colourIndex].Count * bishopValue;
        material += board.Rooks[colourIndex].Count * rookValue;
        material += board.Queens[colourIndex].Count * queenValue;

        return material;
    }

    int EvaluatePieceSquareTables(bool isWhite, float endgameT)
    {
        int value = 0;
        int colourIndex = isWhite ? Board.WhiteIndex : Board.BlackIndex;
        //value += EvaluatePieceSquareTable(PieceSquareTable.Pawns, board.pawns[colourIndex], isWhite);
        value += EvaluatePieceSquareTable(PieceSquareTable.rooks, board.Rooks[colourIndex], isWhite);
        value += EvaluatePieceSquareTable(PieceSquareTable.knights, board.Knights[colourIndex], isWhite);
        value += EvaluatePieceSquareTable(PieceSquareTable.bishops, board.Bishops[colourIndex], isWhite);
        value += EvaluatePieceSquareTable(PieceSquareTable.queens, board.Queens[colourIndex], isWhite);

        int pawnEarly = EvaluatePieceSquareTable(PieceSquareTable.pawns, board.Pawns[colourIndex], isWhite);
        int pawnLate = EvaluatePieceSquareTable(PieceSquareTable.PawnsEnd, board.Pawns[colourIndex], isWhite);
        value += (int)(pawnEarly * (1 - endgameT));
        value += (int)(pawnLate * endgameT);

        int kingEarlyPhase = PieceSquareTable.Read(PieceSquareTable.KingStart, board.KingSquare[colourIndex], isWhite);
        value += (int)(kingEarlyPhase * (1 - endgameT));
        int kingLatePhase = PieceSquareTable.Read(PieceSquareTable.KingEnd, board.KingSquare[colourIndex], isWhite);
        value += (int)(kingLatePhase * (endgameT));

        return value;
    }

    int EvaluatePieceSquareTable(int[] table, PieceList pieceList, bool isWhite)
    {
        int value = 0;
        for (int i = 0; i < pieceList.Count; i++)
        {
            value += PieceSquareTable.Read(table, pieceList[i], isWhite);
        }
        return value;
    }

    public int EvaluatePawns(int colourIndex)
    {
        PieceList pawns = board.Pawns[colourIndex];
        bool isWhite = colourIndex == Board.WhiteIndex;
        ulong opponentPawns = board.PieceBitboards[Piece.MakePiece(Piece.Pawn, isWhite ? Piece.Black : Piece.White)];
        ulong friendlyPawns = board.PieceBitboards[Piece.MakePiece(Piece.Pawn, isWhite ? Piece.White : Piece.Black)];
        ulong[] masks = isWhite ? Bits.WhitePassedPawnMask : Bits.BlackPassedPawnMask;
        int bonus = 0;
        int numIsolatedPawns = 0;

        //Debug.Log((isWhite ? "Black" : "White") + " has no pieces: " + opponentHasNoPieces);

        for (int i = 0; i < pawns.Count; i++)
        {
            int square = pawns[i];
            ulong passedMask = masks[square];
            // Is passed pawn
            if ((opponentPawns & passedMask) == 0)
            {
                int rank = BoardHelper.RankIndex(square);
                int numSquaresFromPromotion = isWhite ? 7 - rank : rank;
                bonus += passedPawnBonuses[numSquaresFromPromotion];
            }

            // Is isolated pawn
            if ((friendlyPawns & Bits.AdjacentFileMasks[BoardHelper.FileIndex(square)]) == 0)
            {
                numIsolatedPawns++;
            }
        }

        return bonus + isolatedPawnPenaltyByCount[numIsolatedPawns];
    }

    int EvaluateKingAttack(bool isWhite)
    {
        ulong[] kingSafetyMasks = isWhite ?  Bits.BlackKingSafetyMask : Bits.WhiteKingSafetyMask;
        int kingSquare = isWhite ? board.KingSquare[1] : board.KingSquare[0];

        ulong kingSafetyMask = kingSafetyMasks[kingSquare];

        moveGenerator.GenerateFriendlyAttackMap(board, isWhite);
        ulong[] friendlyMinorPiecesAttackMap = moveGenerator.friendlyMinorPiecesAttackMap;
        ulong[] friendlyRookAttackMap = moveGenerator.friendlyRooksAttackMap;
        ulong[] friendlyQueensAttackMap = moveGenerator.friendlyQueensAttackMap;
        
        int attackUnit = 0;
        attackUnit += GetAttackUnit(friendlyMinorPiecesAttackMap, kingSafetyMask, 2);
        attackUnit += GetAttackUnit(friendlyRookAttackMap, kingSafetyMask, 3);
        attackUnit += GetAttackUnit(friendlyQueensAttackMap, kingSafetyMask, 5);
        

        return SafetyTable[attackUnit];
    }

    int GetAttackUnit(ulong[] AttackMap, ulong kingMask, int unitPerHit)
    {
        int hit = 0;
        for (int i = 0; i < AttackMap.Length; i++)
        {
            ulong value = AttackMap[i] & kingMask;
            if (value != 0)
            {
                hit++;
            }
        }
        return hit * unitPerHit;
    }


    public struct EvaluationData
    {
        public int materialScore;
        public int mopUpScore;
        public int pieceSquareScore;
        public int pawnScore;
        public int kingAttackScore; // Give point if the other king is under attack

        public int Sum()
        {
            return materialScore + mopUpScore + pieceSquareScore + pawnScore + kingAttackScore;
        }
    }
    MaterialInfo GetMaterialInfo(int colourIndex)
    {
        int numPawns = board.Pawns[colourIndex].Count;
        int numKnights = board.Knights[colourIndex].Count;
        int numBishops = board.Bishops[colourIndex].Count;
        int numRooks = board.Rooks[colourIndex].Count;
        int numQueens = board.Queens[colourIndex].Count;

        bool isWhite = colourIndex == Board.WhiteIndex;
        ulong myPawns = board.PieceBitboards[Piece.MakePiece(Piece.Pawn, isWhite)];
        ulong enemyPawns = board.PieceBitboards[Piece.MakePiece(Piece.Pawn, !isWhite)];

        return new MaterialInfo(numPawns, numKnights, numBishops, numQueens, numRooks, myPawns, enemyPawns);
    }

    public readonly struct MaterialInfo
    {
        public readonly int materialScore;
        public readonly int numPawns;
        public readonly int numMajors;
        public readonly int numMinors;
        public readonly int numBishops;
        public readonly int numQueens;
        public readonly int numRooks;

        public readonly ulong pawns;
        public readonly ulong enemyPawns;

        public readonly float endgameT;

        public MaterialInfo(int numPawns, int numKnights, int numBishops, int numQueens, int numRooks, ulong myPawns, ulong enemyPawns)
        {
            this.numPawns = numPawns;
            this.numBishops = numBishops;
            this.numQueens = numQueens;
            this.numRooks = numRooks;
            this.pawns = myPawns;
            this.enemyPawns = enemyPawns;

            numMajors = numRooks + numQueens;
            numMinors = numBishops + numKnights;

            materialScore = 0;
            materialScore += numPawns * pawnValue;
            materialScore += numKnights * knightValue;
            materialScore += numBishops * bishopValue;
            materialScore += numRooks * rookValue;
            materialScore += numQueens * queenValue;

            // Endgame Transition (0->1)
            const int queenEndgameWeight = 45;
            const int rookEndgameWeight = 20;
            const int bishopEndgameWeight = 10;
            const int knightEndgameWeight = 10;

            const int endgameStartWeight = 2 * rookEndgameWeight + 2 * bishopEndgameWeight + 2 * knightEndgameWeight + queenEndgameWeight;
            int endgameWeightSum = numQueens * queenEndgameWeight + numRooks * rookEndgameWeight + numBishops * bishopEndgameWeight + numKnights * knightEndgameWeight;
            endgameT = 1 - System.Math.Min(1, endgameWeightSum / (float)endgameStartWeight);
        }
    }
    int MopUpEval(bool isWhite, MaterialInfo myMaterial, MaterialInfo enemyMaterial)
    {
        if (myMaterial.materialScore > enemyMaterial.materialScore + pawnValue * 2 && enemyMaterial.endgameT > 0)
        {
            int mopUpScore = 0;
            int friendlyIndex = isWhite ? Board.WhiteIndex : Board.BlackIndex;
            int opponentIndex = isWhite ? Board.BlackIndex : Board.WhiteIndex;

            int friendlyKingSquare = board.KingSquare[friendlyIndex];
            int opponentKingSquare = board.KingSquare[opponentIndex];
            // Encourage moving king closer to opponent king
            mopUpScore += (14 - PrecomputedMoveData.OrthogonalDistance[friendlyKingSquare, opponentKingSquare]) * 4;
            // Encourage pushing opponent king to edge of board
            mopUpScore += PrecomputedMoveData.CentreManhattanDistance[opponentKingSquare] * 10;
            return (int)(mopUpScore * enemyMaterial.endgameT);
        }
        return 0;
    }
}
