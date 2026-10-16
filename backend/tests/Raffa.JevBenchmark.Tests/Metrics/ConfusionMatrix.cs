namespace Raffa.JevBenchmark.Tests.Metrics;

public sealed record ClassMetrics(string Label, int Support, int TruePositives, int FalsePositives, int FalseNegatives, double Precision, double Recall, double F1);

/// <summary>
/// Confusion matrix over a fixed label set. Rows are the expected label, columns the predicted one,
/// plus a final "(error)" column for calls that produced no prediction: an error is never silently
/// dropped from recall (the case was not answered), but it is not a false positive of any class either.
/// </summary>
public sealed class ConfusionMatrix
{
    public const string ErrorColumn = "(error)";

    private readonly string[] _labels;
    private readonly int[,] _counts;

    public ConfusionMatrix(IReadOnlyList<string> labels)
    {
        _labels = labels.ToArray();
        _counts = new int[_labels.Length, _labels.Length + 1];
    }

    public IReadOnlyList<string> Labels => _labels;

    public int Total { get; private set; }

    public void Add(string expected, string? predicted)
    {
        var row = Array.IndexOf(_labels, expected);
        if (row < 0)
        {
            throw new ArgumentException($"Expected label '{expected}' is not in the label set.", nameof(expected));
        }

        var column = predicted is null ? _labels.Length : Array.IndexOf(_labels, predicted);
        if (column < 0)
        {
            throw new ArgumentException($"Predicted label '{predicted}' is not in the label set.", nameof(predicted));
        }

        _counts[row, column]++;
        Total++;
    }

    public int Count(string expected, string? predicted) =>
        _counts[Array.IndexOf(_labels, expected), predicted is null ? _labels.Length : Array.IndexOf(_labels, predicted)];

    /// <summary>Correct over all calls, errors counted as wrong.</summary>
    public double StrictAccuracy => Total == 0 ? double.NaN : Enumerable.Range(0, _labels.Length).Sum(i => _counts[i, i]) / (double)Total;

    public int Answered => Total - Enumerable.Range(0, _labels.Length).Sum(i => _counts[i, _labels.Length]);

    /// <summary>Correct over the calls that produced a prediction.</summary>
    public double AnsweredAccuracy => Answered == 0 ? double.NaN : Enumerable.Range(0, _labels.Length).Sum(i => _counts[i, i]) / (double)Answered;

    public IReadOnlyList<ClassMetrics> PerClass()
    {
        var result = new List<ClassMetrics>(_labels.Length);
        for (var i = 0; i < _labels.Length; i++)
        {
            var tp = _counts[i, i];
            var support = Enumerable.Range(0, _labels.Length + 1).Sum(j => _counts[i, j]);
            var predictedAs = Enumerable.Range(0, _labels.Length).Sum(r => _counts[r, i]);
            var fp = predictedAs - tp;
            var fn = support - tp;
            var precision = predictedAs == 0 ? double.NaN : tp / (double)predictedAs;
            var recall = support == 0 ? double.NaN : tp / (double)support;
            var f1 = double.IsNaN(precision) || double.IsNaN(recall) || precision + recall == 0
                ? (support == 0 && predictedAs == 0 ? double.NaN : 0.0)
                : 2 * precision * recall / (precision + recall);
            result.Add(new ClassMetrics(_labels[i], support, tp, fp, fn, precision, recall, f1));
        }

        return result;
    }

    /// <summary>Unweighted mean F1 over the classes that occur in the labels (support &gt; 0).</summary>
    public double MacroF1
    {
        get
        {
            var present = PerClass().Where(c => c.Support > 0).ToList();
            return present.Count == 0 ? double.NaN : present.Average(c => double.IsNaN(c.F1) ? 0 : c.F1);
        }
    }

    /// <summary>Rows as plain arrays for serialization: <c>matrix[expected][predicted-or-error]</c>.</summary>
    public int[][] ToRows() =>
        Enumerable.Range(0, _labels.Length)
            .Select(i => Enumerable.Range(0, _labels.Length + 1).Select(j => _counts[i, j]).ToArray())
            .ToArray();
}
