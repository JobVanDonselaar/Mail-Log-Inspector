namespace MailLogInspector.Core;

public sealed record MailLogInspectorBreakdownRow(
	string Key,
	int Total,
	int Delivered,
	int Underway,
	int Bounce)
{
	public int ProblemCount => Underway + Bounce;

	public double SuccessRate => Total <= 0 ? 0.0 : (double)Delivered / Total;

	public double ProblemRate => Total <= 0 ? 0.0 : (double)ProblemCount / Total;

	public string SuccessRateDisplay => $"{SuccessRate * 100.0:0.0}%";

	public string ProblemRateDisplay => $"{ProblemRate * 100.0:0.0}%";
}
