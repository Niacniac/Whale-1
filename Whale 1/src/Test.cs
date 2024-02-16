using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Whale_1.src.Core.AI_Scripts;

[MemoryDiagnoser]
public class Test
{
    private Board board;
    private Evaluation myClassInstance;
    private Move move;
    private List<int> list = new List<int>();
    private List<int>[] lists = new List<int>[2];
    //[Params(true, false)]
    //public bool UseNNUEeval { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Initialize your data here
        string benchFEN = "rnb1k1nr/pRpp1pRp/7K/1NbPqPpB/Q1PP1N2/B3p3/P5PP/8 w kq - 0 1";
        board = new Board();
        board.LoadPosition(benchFEN);
        myClassInstance = new Evaluation();
        move = new Move(33, 50);
        lists[0] = new List<int>();
        lists[1] = new List<int>();
        
    }

    [Benchmark]
    public void BenchmarkRefresh()
    {
        myClassInstance.nnue.SetAccumulatorFromBoard(board, 0);
        myClassInstance.nnue.SetAccumulatorFromBoard(board, 1);
        //myClassInstance.Evaluate(board, UseNNUEeval);
    }

    [Benchmark]
    public void BenchmarkUpdate()
    {
        myClassInstance.nnue.TryUpdateAccumulators(board, false);
    }

    [Benchmark]
    public void BenchmarkHalfKPCreate()
    {
        NNUE.HalfKP.CreateActiveIndices(board, 0, list);
    }

    [Benchmark]
    public void BenchmarkHalfKPappened() 
    {
        NNUE.HalfKP.AppendChangedIndices(lists, lists, move, board, 0, false);
    }
}

