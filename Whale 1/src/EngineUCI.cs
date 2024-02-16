
using BenchmarkDotNet.Running;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Whale_1.src.Core.AI_Scripts;

public class EngineUCI
{
	readonly Bot player;
	static readonly bool logToFile = false;

	static readonly string[] positionLabels = new[] { "position", "fen", "moves" };
	static readonly string[] goLabels = new[] { "go", "movetime", "wtime", "btime", "winc", "binc", "movestogo" };

	public EngineUCI()
	{
		player = new Bot();
		player.OnMoveChosen += OnMoveChosen;
	}

	public void ReceiveCommand(string message)
	{
		//Console.WriteLine(message);
		LogToFile("Command received: " + message);
		message = message.Trim();
		string messageType = message.Split(' ')[0].ToLower();

		switch (messageType)
		{
			case "uci":
				RespondUCI();
				break;
			case "isready":
				Respond("readyok");
				break;
			case "ucinewgame":
				player.NotifyNewGame();
				break;
			case "position":
				ProcessPositionCommand(message);
				break;
			case "go":
				ProcessGoCommand(message);
				break;
			case "setoption":
				ProcessOptionCommand(message);
				break;
			case "test":
				ProcessTestCommand(message);
				break;
			case "stop":
				if (player.IsThinking)
				{
					player.StopThinking();
				}
				break;
			case "quit":
				player.Quit();
				break;
			case "d":
				Console.WriteLine(player.GetBoardDiagram());
				break;
			default:
				LogToFile($"Unrecognized command: {messageType}");
				break;
		}
	}

	void OnMoveChosen(string move)
	{
		LogToFile("OnMoveChosen: book move = " + player.LatestMoveIsBookMove);
		Respond("bestmove " + move);
	}

	void ProcessTestCommand(string message)
	{
		Test test = new Test();
		test.GlobalSetup();
		test.BenchmarkHalfKPappened();
		test.BenchmarkHalfKPCreate();
        var summary = BenchmarkRunner.Run<Test>();
    }

	void ProcessOptionCommand(string message)
	{
		if (!message.Split(' ')[1].Equals("name", StringComparison.CurrentCultureIgnoreCase))
		{
			Respond("Invalid syntax");
			Respond("syntax should be : setoption name <id> [value <x>]");
			return;
		}

		string messageID = message.Split(' ')[2].ToLower();
		string messageValue = message.Split(' ')[3].ToLower();

        switch (messageID)
		{
			case "hash":
				if (message.Split(' ')[3].Equals("value", StringComparison.CurrentCultureIgnoreCase))
				{
                    messageValue = message.Split(' ')[4].ToLower();
                    player.SetOption(0, int.Parse(messageValue));
                }
				break;
			case "threads":
				if (message.Split(' ')[3].Equals("value", StringComparison.CurrentCultureIgnoreCase))
				{
                    messageValue = message.Split(' ')[4].ToLower();
                    player.SetOption(1, int.Parse(messageValue));
                }            
                break;
			case "use":
                if (message.Split(' ')[3].Equals("nnue", StringComparison.CurrentCultureIgnoreCase)
					&& message.Split(' ')[4].Equals("value", StringComparison.CurrentCultureIgnoreCase))
				{
                    messageValue = message.Split(' ')[5].ToLower();
					bool messageValuebool = bool.Parse(messageValue);
					player.SetOption(2, Convert.ToInt32(messageValuebool));
                }
                break;
			default:
				Respond("Invalid option name");
				Respond("type 'uci' to show the available options");
				break;
		}
	}

	void ProcessGoCommand(string message)
	{
		if (message.Contains("movetime"))
		{
			int moveTimeMs = TryGetLabelledValueInt(message, "movetime", goLabels, 0);
			player.ThinkTimed(moveTimeMs);
		}
		else if (message.Contains("infinite"))
		{
			player.ThinkTimed(0, true);
		}
		else
		{
			int timeRemainingWhiteMs = TryGetLabelledValueInt(message, "wtime", goLabels, 0);
			int timeRemainingBlackMs = TryGetLabelledValueInt(message, "btime", goLabels, 0);
			int incrementWhiteMs = TryGetLabelledValueInt(message, "winc", goLabels, 0);
			int incrementBlackMs = TryGetLabelledValueInt(message, "binc", goLabels, 0);

			int thinkTime = player.ChooseThinkTime(timeRemainingWhiteMs, timeRemainingBlackMs, incrementWhiteMs, incrementBlackMs);
			LogToFile("Thinking for: " + thinkTime + " ms.");
			player.ThinkTimed(thinkTime);
		}

	}

	// Format: 'position startpos moves e2e4 e7e5'
	// Or: 'position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 moves e2e4 e7e5'
	// Note: 'moves' section is optional
	void ProcessPositionCommand(string message)
	{
		// FEN
		if (message.ToLower().Contains("startpos"))
		{
			player.SetPosition(FenUtility.StartPositionFEN);
		}
		else if (message.ToLower().Contains("fen")) {
			try
			{
                string customFen = TryGetLabelledValue(message, "fen", positionLabels);
                player.SetPosition(customFen);
            }
			catch { Console.WriteLine("Invalid fen syntax"); }
		}
		else
		{
			Console.WriteLine("Invalid position command (expected 'startpos' or 'fen')");
		}

		// Moves
		string allMoves = TryGetLabelledValue(message, "moves", positionLabels);
		if (!string.IsNullOrEmpty(allMoves))
		{
			string[] moveList = allMoves.Split(' ');
			foreach (string move in moveList)
			{
				player.MakeMove(move);
			}

			LogToFile($"Make moves after setting position: {moveList.Length}");
		}
	}

	void Respond(string reponse)
	{
		Console.WriteLine(reponse);
		LogToFile("Response sent: " + reponse);
	}
	void RespondUCI()
	{
		Respond("id name Whale 7");
		Respond("id author Niacniac");
		Console.WriteLine();
		Respond("option name Threads type spin default 1 min 1 max 128");
		Respond("option name Hash type spin default 16 min 1 max 32000");
		Respond("option name Use NNUE type check default true");
		Respond("uciok");	
	}

	static int TryGetLabelledValueInt(string text, string label, string[] allLabels, int defaultValue = 0)
	{
		string valueString = TryGetLabelledValue(text, label, allLabels, defaultValue + "");
		if (int.TryParse(valueString.Split(' ')[0], out int result))
		{
			return result;
		}
		return defaultValue;
	}

	static string TryGetLabelledValue(string text, string label, string[] allLabels, string defaultValue = "")
	{
		text = text.Trim();
		if (text.Contains(label))
		{
			int valueStart = text.IndexOf(label) + label.Length;
			int valueEnd = text.Length;
			foreach (string otherID in allLabels)
			{
				if (otherID != label && text.Contains(otherID))
				{
					int otherIDStartIndex = text.IndexOf(otherID);
					if (otherIDStartIndex > valueStart && otherIDStartIndex < valueEnd)
					{
						valueEnd = otherIDStartIndex;
					}
				}
			}

			return text.Substring(valueStart, valueEnd - valueStart).Trim();
		}
		return defaultValue;
	}

	void LogToFile(string text)
	{
		if (logToFile)
		{
			Directory.CreateDirectory(AppDataPath);
			string path = Path.Combine(AppDataPath, "UCI_Log.txt");

			using (StreamWriter writer = new StreamWriter(path, true))
			{
				writer.WriteLine(text);
			}
		}
	}

	public static string AppDataPath
	{
		get
		{
			string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine(dir, "Chess-Coding-Adventure");
		}
	}

}
