# Overview
Whale is a stong chess engine written entirely in C#. This engine incorporates a neural network evaluation (NNUE) inspired by Stockfish. 
# Features

 - The search function is based on alpha-beta prunning with a principle variation search (PVS). It uses the common prunning technics like null-move prunning, futility-move prunning and a transposition table. It also support multithreaded search.
 - The evaluation is either classic or with the NNUE which is much stronger. Processor supporting Avx2 is required for the NNUE to work.
 - Compatible with the Universal Chess Interface (UCI) protocol with basic commands. (more will be added)
# Credit
 - The NNUE is based on Stockfish architecture [SFNNv1](https://github.com/official-stockfish/nnue-pytorch/blob/master/docs/nnue.md#sfnnv1-architecture) of Stockfish 12 and uses Stockfish network file nn-62ef826d1a6d.nnue
 - The based structure of the engine has been taken from Sebastian Lague : [Chess Coding Adventure](https://github.com/SebLague/Chess-Coding-Adventure)

