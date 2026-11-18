using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IAiTestService
{
    Task<AiTestSession> GenerateTestAsync(
        string topic,
        int questionCount,
        double durationHours,
        string difficulty,
        string targetExam,
        string? pdfContext = null,
        TestSourceKind sourceKind = TestSourceKind.Topic,
        string? sourceDocumentName = null,
        Action<string>? onStatusUpdate = null,
        CancellationToken cancellationToken = default);

    Task<TestEvaluationReport> EvaluateTestAsync(
        AiTestSession session,
        Action<string>? onStatusUpdate = null,
        CancellationToken cancellationToken = default);

    Task SaveTestSessionAsync(AiTestSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiTestSession>> LoadAllTestsAsync(CancellationToken cancellationToken = default);
    Task DeleteTestSessionAsync(string testId, CancellationToken cancellationToken = default);
}

public sealed class AiTestService : IAiTestService
{
    private readonly IAiChatService _chatService;
    private readonly IAiSettingsService _settingsService;
    private readonly IWebResearchService _webResearchService;
    private readonly string _testsDirectory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AiTestService(
        IAiChatService chatService,
        IAiSettingsService settingsService,
        IWebResearchService webResearchService,
        string? customDirectory = null)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        _webResearchService = webResearchService;

        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            _testsDirectory = customDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _testsDirectory = Path.Combine(appData, "StudySuit.PdfReader", "tests");
        }

        try
        {
            Directory.CreateDirectory(_testsDirectory);
        }
        catch { }
    }

    public async Task<AiTestSession> GenerateTestAsync(
        string topic,
        int questionCount,
        double durationHours,
        string difficulty,
        string targetExam,
        string? pdfContext = null,
        TestSourceKind sourceKind = TestSourceKind.Topic,
        string? sourceDocumentName = null,
        Action<string>? onStatusUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        if (questionCount < 1) questionCount = 5;
        if (questionCount > 50) questionCount = 50;
        if (durationHours <= 0) durationHours = 0.5;

        string effectiveTopic = !string.IsNullOrWhiteSpace(topic) ? topic : (sourceDocumentName ?? "Academic Test");

        // 1. Web research grounding (only when topic-based and no local PDF context is provided)
        IReadOnlyList<WebSourceReference> webSources = Array.Empty<WebSourceReference>();
        if (string.IsNullOrWhiteSpace(pdfContext))
        {
            onStatusUpdate?.Invoke($"Searching academic syllabus and exam sources for \"{effectiveTopic}\"...");
            try
            {
                string searchQuery = $"{effectiveTopic} {targetExam} exam questions syllabus concepts";
                webSources = await _webResearchService.SearchAsync(searchQuery, settings, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Fall back without web sources if search fails
            }
        }
        else
        {
            onStatusUpdate?.Invoke($"Analyzing document text from \"{sourceDocumentName ?? "selected PDF"}\"...");
        }

        onStatusUpdate?.Invoke($"Formulating {questionCount} multiple-choice & reasoning questions with AI ({settings.Provider})...");

        // 2. Build structured prompt with reasoning requirements and PDF grounding
        string sourceDirective = !string.IsNullOrWhiteSpace(pdfContext)
            ? $@"CRITICAL PDF GROUNDING REQUIREMENT:
The questions MUST be generated directly and exclusively from the provided PDF document text below.
- ALL questions, principles, assertions, reasons, statements, and facts MUST be rooted in this PDF.
- Do NOT test outside facts or unrelated topics absent from this document.
- Thoroughly test the student's mastery and comprehension of this specific document."
            : "";

        string prompt = $@"You are an elite academic exam master and curriculum specialist.
Your task is to generate a comprehensive, highly authentic examination featuring both multiple-choice and topic-based reasoning questions on: ""{effectiveTopic}"".

Target Exam/Context: {targetExam}
Difficulty Level: {difficulty}
Number of Questions: {questionCount}
Duration: {durationHours} hours

{sourceDirective}

QUESTION TYPES & REASONING FORMATS (MANDATORY VARIETY):
You MUST provide a balanced mix of question formats across the {questionCount} questions:

1. Assertion & Reasoning Questions (""questionType"": ""assertion_reason""):
   - Provide ""assertion"": string (Assertion statement A)
   - Provide ""reason"": string (Reason statement R)
   - ""questionText"": ""Given below are two statements: Assertion (A) and Reason (R).""
   - Options MUST follow the standard 4-choice pattern:
     A) Both Assertion (A) and Reason (R) are true, and Reason (R) is the correct explanation of Assertion (A).
     B) Both Assertion (A) and Reason (R) are true, but Reason (R) is NOT the correct explanation of Assertion (A).
     C) Assertion (A) is true, but Reason (R) is false.
     D) Assertion (A) is false, but Reason (R) is true.

2. Statement-Based Reasoning Questions (""questionType"": ""statement_reasoning""):
   - Provide ""statement1"": string (Statement I)
   - Provide ""statement2"": string (Statement II)
   - ""questionText"": ""Evaluate the correctness of the following two statements:""
   - Options MUST follow the standard pattern:
     A) Both Statement I and Statement II are correct.
     B) Both Statement I and Statement II are incorrect.
     C) Statement I is correct but Statement II is incorrect.
     D) Statement I is incorrect but Statement II is correct.

3. Case-Based / Scenario Reasoning Questions (""questionType"": ""case_reasoning""):
   - Provide ""contextOrCase"": string (A clear scenario, experimental setup, or contextual observation).
   - ""questionText"": analytical or evaluative deduction question testing the case.
   - 4 distinct, plausible options evaluating outcomes, mechanisms, or principles.

4. Conceptual Analytical MCQs (""questionType"": ""mcq""):
   - High-yield multiple choice questions testing principles, derivations, and definitions with 4 distinct options.

GENERAL RULES:
1. Provide EXACTLY {questionCount} questions.
2. Each question MUST have EXACTLY 4 distinct options (Option A, Option B, Option C, Option D).
3. Exactly ONE option must be correct.
4. Set ""correctOptionIndex"" as a 0-based integer (0 = A, 1 = B, 2 = C, 3 = D).
5. Provide a detailed, step-by-step educational ""explanation"" for why the correct option is right and why others are incorrect.
6. Output MUST be ONLY valid JSON matching this schema:

```json
{{
  ""title"": ""{effectiveTopic} Mock Test"",
  ""topic"": ""{effectiveTopic}"",
  ""questions"": [
    {{
      ""number"": 1,
      ""questionType"": ""assertion_reason"",
      ""questionText"": ""Given below are two statements: one is labelled as Assertion (A) and the other is labelled as Reason (R)."",
      ""assertion"": ""Assertion statement text here"",
      ""reason"": ""Reason statement text here"",
      ""options"": [
        ""Both Assertion (A) and Reason (R) are true, and Reason (R) is the correct explanation of Assertion (A)."",
        ""Both Assertion (A) and Reason (R) are true, but Reason (R) is NOT the correct explanation of Assertion (A)."",
        ""Assertion (A) is true, but Reason (R) is false."",
        ""Assertion (A) is false, but Reason (R) is true.""
      ],
      ""correctOptionIndex"": 0,
      ""subtopic"": ""Subtopic Name"",
      ""explanation"": ""Detailed step-by-step solution and reasoning.""
    }},
    {{
      ""number"": 2,
      ""questionType"": ""statement_reasoning"",
      ""questionText"": ""Given below are two statements regarding {effectiveTopic}:"",
      ""statement1"": ""Statement I text here"",
      ""statement2"": ""Statement II text here"",
      ""options"": [
        ""Both Statement I and Statement II are correct."",
        ""Both Statement I and Statement II are incorrect."",
        ""Statement I is correct but Statement II is incorrect."",
        ""Statement I is incorrect but Statement II is correct.""
      ],
      ""correctOptionIndex"": 0,
      ""subtopic"": ""Subtopic Name"",
      ""explanation"": ""Detailed analysis of the statements.""
    }},
    {{
      ""number"": 3,
      ""questionType"": ""mcq"",
      ""questionText"": ""Clear conceptual question statement"",
      ""options"": [
        ""Option A text"",
        ""Option B text"",
        ""Option C text"",
        ""Option D text""
      ],
      ""correctOptionIndex"": 1,
      ""subtopic"": ""Subtopic Name"",
      ""explanation"": ""Comprehensive explanation.""
    }}
  ]
}}
```";

        string response = await _chatService.SendChatAsync(
            settings,
            history: Array.Empty<AiChatMessage>(),
            userPrompt: prompt,
            pdfContext: pdfContext,
            webSources: webSources,
            onStreamDelta: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        onStatusUpdate?.Invoke("Parsing and verifying generated test structure...");

        var session = ParseGeneratedTestJson(response, effectiveTopic, questionCount, durationHours, difficulty, targetExam, sourceKind, sourceDocumentName);
        await SaveTestSessionAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<TestEvaluationReport> EvaluateTestAsync(
        AiTestSession session,
        Action<string>? onStatusUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        onStatusUpdate?.Invoke("Analyzing test performance and generating detailed AI feedback...");

        // Build summary of candidate responses for AI evaluation
        var details = new System.Text.StringBuilder();
        details.AppendLine($"Topic: {session.Topic}");
        details.AppendLine($"Total Questions: {session.TotalQuestions}");
        details.AppendLine($"Score: {session.CorrectCount}/{session.TotalQuestions} ({session.ScorePercentage}%)");
        details.AppendLine($"Time Spent: {session.FormattedTimeSpent} of {session.FormattedDuration}");
        details.AppendLine();
        details.AppendLine("Question Performance Breakdown:");

        for (int i = 0; i < session.Questions.Count; i++)
        {
            var q = session.Questions[i];
            string status = !q.IsAnswered ? "UNANSWERED" : (q.IsCorrect ? "CORRECT" : "INCORRECT");
            string userAns = q.IsAnswered && q.UserSelectedOptionIndex.HasValue && q.UserSelectedOptionIndex.Value < q.Options.Count
                ? $"{q.SelectedOptionLabel}: {q.Options[q.UserSelectedOptionIndex.Value]}"
                : "None";
            string correctAns = q.CorrectOptionIndex >= 0 && q.CorrectOptionIndex < q.Options.Count
                ? $"{q.CorrectOptionLabel}: {q.Options[q.CorrectOptionIndex]}"
                : "Option A";

            details.AppendLine($"Q{i + 1} [{q.Subtopic}] - Result: {status}");
            details.AppendLine($"  Statement: {q.QuestionText}");
            details.AppendLine($"  Candidate Answer: {userAns}");
            details.AppendLine($"  Correct Answer: {correctAns}");
        }

        string prompt = $@"You are an empathetic, world-class academic tutor and test analyst.
Analyze the following test attempt by a student and produce a deeply actionable, insightful, and motivating performance report.

{details}

OUTPUT REQUIREMENTS:
Return ONLY valid JSON matching this schema:
```json
{{
  ""summary"": ""A clear 2-3 sentence executive summary of candidate performance, strengths, and primary area to focus on."",
  ""overallAssessment"": ""In-depth analysis of candidate conceptual mastery, exam strategy, pacing, and readiness."",
  ""strongAreas"": [
    ""Specific topic or skill candidate demonstrated high accuracy in"",
    ""Another strong concept""
  ],
  ""weakAreas"": [
    ""Specific subtopic, formula, or concept where errors occurred"",
    ""Another area needing conceptual strengthening""
  ],
  ""actionableSuggestions"": [
    ""Concrete, step-by-step revision technique or method"",
    ""Specific practice recommendation"",
    ""Exam temperament and elimination strategy advice""
  ],
  ""recommendedTopicsToReview"": [
    ""Subtopic 1"",
    ""Subtopic 2"",
    ""Subtopic 3""
  ]
}}
```";

        try
        {
            string response = await _chatService.SendChatAsync(
                settings,
                history: Array.Empty<AiChatMessage>(),
                userPrompt: prompt,
                pdfContext: null,
                webSources: Array.Empty<WebSourceReference>(),
                onStreamDelta: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var report = ParseEvaluationJson(response, session);
            session.EvaluationReport = report;
        }
        catch (Exception ex)
        {
            // Fallback generated evaluation
            session.EvaluationReport = CreateFallbackReport(session, ex.Message);
        }

        session.CompletedAt = DateTime.UtcNow;
        if (session.TimeRemaining <= TimeSpan.Zero)
        {
            session.Status = TestStatus.TimedOut;
        }
        else
        {
            session.Status = TestStatus.Completed;
        }

        session.NotifyScoresChanged();
        await SaveTestSessionAsync(session, cancellationToken).ConfigureAwait(false);
        return session.EvaluationReport;
    }

    public async Task SaveTestSessionAsync(AiTestSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            string filePath = Path.Combine(_testsDirectory, $"{session.Id}.json");
            string json = JsonSerializer.Serialize(session, JsonOpts);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiTestService] Failed to save session: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AiTestSession>> LoadAllTestsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<AiTestSession>();
        try
        {
            if (!Directory.Exists(_testsDirectory))
            {
                return list;
            }

            var files = Directory.GetFiles(_testsDirectory, "*.json");
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var session = JsonSerializer.Deserialize<AiTestSession>(json, JsonOpts);
                    if (session != null)
                    {
                        list.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiTestService] Error loading {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiTestService] LoadAllTests error: {ex.Message}");
        }

        return list.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public Task DeleteTestSessionAsync(string testId, CancellationToken cancellationToken = default)
    {
        try
        {
            string filePath = Path.Combine(_testsDirectory, $"{testId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    private static AiTestSession ParseGeneratedTestJson(
        string rawResponse,
        string topic,
        int expectedCount,
        double durationHours,
        string difficulty,
        string targetExam,
        TestSourceKind sourceKind = TestSourceKind.Topic,
        string? sourceDocumentName = null)
    {
        string json = ExtractJson(rawResponse);
        var session = new AiTestSession
        {
            Topic = topic,
            Title = $"{topic} Mock Test",
            Difficulty = difficulty,
            TargetExam = targetExam,
            SourceKind = sourceKind,
            SourceDocumentName = sourceDocumentName,
            DurationHours = durationHours,
            TotalDuration = TimeSpan.FromHours(durationHours),
            TimeRemaining = TimeSpan.FromHours(durationHours),
            Status = TestStatus.NotStarted
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("title", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                session.Title = titleProp.GetString()!;
            }

            if (root.TryGetProperty("questions", out var questionsProp) && questionsProp.ValueKind == JsonValueKind.Array)
            {
                int qNum = 1;
                foreach (var qElem in questionsProp.EnumerateArray())
                {
                    string qText = qElem.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(qText) && qElem.TryGetProperty("question", out var qAlt))
                    {
                        qText = qAlt.GetString() ?? "";
                    }

                    string qType = qElem.TryGetProperty("questionType", out var qtType) ? qtType.GetString() ?? "mcq" :
                                   (qElem.TryGetProperty("type", out var tType) ? tType.GetString() ?? "mcq" : "mcq");

                    string? assertion = qElem.TryGetProperty("assertion", out var assProp) ? assProp.GetString() : null;
                    string? reason = qElem.TryGetProperty("reason", out var rProp) ? rProp.GetString() : null;
                    string? statement1 = qElem.TryGetProperty("statement1", out var s1Prop) ? s1Prop.GetString() :
                                        (qElem.TryGetProperty("statementOne", out var s1Alt) ? s1Alt.GetString() : null);
                    string? statement2 = qElem.TryGetProperty("statement2", out var s2Prop) ? s2Prop.GetString() :
                                        (qElem.TryGetProperty("statementTwo", out var s2Alt) ? s2Alt.GetString() : null);
                    string? contextOrCase = qElem.TryGetProperty("contextOrCase", out var cProp) ? cProp.GetString() :
                                           (qElem.TryGetProperty("context", out var cAlt) ? cAlt.GetString() :
                                           (qElem.TryGetProperty("passage", out var pAlt) ? pAlt.GetString() : null));

                    // Regex fallbacks if JSON didn't separate them
                    if (string.IsNullOrWhiteSpace(assertion) && string.IsNullOrWhiteSpace(reason) && !string.IsNullOrWhiteSpace(qText))
                    {
                        var arMatch = Regex.Match(qText, @"Assertion\s*(?:\(A\)|A)?\s*[:\-]\s*(?<ass>.+?)\s*Reason\s*(?:\(R\)|R)?\s*[:\-]\s*(?<rea>.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        if (arMatch.Success)
                        {
                            assertion = arMatch.Groups["ass"].Value.Trim();
                            reason = arMatch.Groups["rea"].Value.Trim();
                            qType = "assertion_reason";
                        }
                    }

                    if (string.IsNullOrWhiteSpace(statement1) && string.IsNullOrWhiteSpace(statement2) && !string.IsNullOrWhiteSpace(qText))
                    {
                        var stmtMatch = Regex.Match(qText, @"Statement\s*(?:I|1)\s*[:\-]\s*(?<s1>.+?)\s*Statement\s*(?:II|2)\s*[:\-]\s*(?<s2>.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        if (stmtMatch.Success)
                        {
                            statement1 = stmtMatch.Groups["s1"].Value.Trim();
                            statement2 = stmtMatch.Groups["s2"].Value.Trim();
                            qType = "statement_reasoning";
                        }
                    }

                    if (string.IsNullOrWhiteSpace(contextOrCase) && !string.IsNullOrWhiteSpace(qText))
                    {
                        var caseMatch = Regex.Match(qText, @"(?:Context|Passage|Case\s*Study|Scenario)\s*[:\-]\s*(?<ctx>.+?)(?:Question|\n\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        if (caseMatch.Success && caseMatch.Groups["ctx"].Value.Length > 20)
                        {
                            contextOrCase = caseMatch.Groups["ctx"].Value.Trim();
                            qType = "case_reasoning";
                        }
                    }

                    var options = new List<string>();
                    if (qElem.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var opt in opts.EnumerateArray())
                        {
                            string optStr = opt.GetString() ?? "";
                            // Strip leading A), B), 1. if present
                            optStr = Regex.Replace(optStr, @"^[A-Da-d1-4][\)\.\:\-]\s*", "").Trim();
                            options.Add(optStr);
                        }
                    }

                    // Pad to 4 options if fewer returned
                    while (options.Count < 4)
                    {
                        options.Add($"Option {options.Count + 1}");
                    }

                    int correctIndex = 0;
                    if (qElem.TryGetProperty("correctOptionIndex", out var ci))
                    {
                        if (ci.ValueKind == JsonValueKind.Number)
                        {
                            correctIndex = ci.GetInt32();
                        }
                        else if (ci.ValueKind == JsonValueKind.String)
                        {
                            string s = ci.GetString()?.Trim().ToUpperInvariant() ?? "A";
                            correctIndex = s switch
                            {
                                "A" or "0" => 0,
                                "B" or "1" => 1,
                                "C" or "2" => 2,
                                "D" or "3" => 3,
                                _ => 0
                            };
                        }
                    }
                    else if (qElem.TryGetProperty("correctAnswer", out var ca))
                    {
                        string s = ca.GetString()?.Trim().ToUpperInvariant() ?? "A";
                        correctIndex = s.StartsWith('B') ? 1 : (s.StartsWith('C') ? 2 : (s.StartsWith('D') ? 3 : 0));
                    }

                    if (correctIndex < 0 || correctIndex >= options.Count) correctIndex = 0;

                    string explanation = qElem.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : "";
                    string subtopic = qElem.TryGetProperty("subtopic", out var sub) ? sub.GetString() ?? "" : topic;

                    session.Questions.Add(new TestQuestion
                    {
                        Number = qNum++,
                        QuestionText = qText,
                        QuestionType = qType,
                        Assertion = assertion,
                        Reason = reason,
                        Statement1 = statement1,
                        Statement2 = statement2,
                        ContextOrCase = contextOrCase,
                        Options = options,
                        CorrectOptionIndex = correctIndex,
                        Explanation = explanation,
                        Subtopic = subtopic
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiTestService] JSON parse warning: {ex.Message}");
        }

        // If JSON parsing yielded 0 questions, synthesize basic questions
        if (session.Questions.Count == 0)
        {
            for (int i = 1; i <= expectedCount; i++)
            {
                session.Questions.Add(new TestQuestion
                {
                    Number = i,
                    QuestionText = $"Conceptual Question {i} on {topic}: Analyze the fundamental principles.",
                    Options =
                    [
                        "Primary theoretical assertion",
                        "Secondary alternative perspective",
                        "Common misconception / counter-argument",
                        "Experimental observation limit"
                    ],
                    CorrectOptionIndex = 0,
                    Explanation = $"Detailed solution for {topic} principles.",
                    Subtopic = topic
                });
            }
        }

        session.NotifyScoresChanged();
        return session;
    }

    private static TestEvaluationReport ParseEvaluationJson(string rawResponse, AiTestSession session)
    {
        string json = ExtractJson(rawResponse);
        var report = new TestEvaluationReport();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("summary", out var sProp))
            {
                report.Summary = sProp.GetString() ?? "";
            }
            if (root.TryGetProperty("overallAssessment", out var oaProp))
            {
                report.OverallAssessment = oaProp.GetString() ?? "";
            }

            if (root.TryGetProperty("strongAreas", out var saProp) && saProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in saProp.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(item.GetString()))
                        report.StrongAreas.Add(item.GetString()!);
                }
            }

            if (root.TryGetProperty("weakAreas", out var waProp) && waProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in waProp.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(item.GetString()))
                        report.WeakAreas.Add(item.GetString()!);
                }
            }

            if (root.TryGetProperty("actionableSuggestions", out var asProp) && asProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in asProp.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(item.GetString()))
                        report.ActionableSuggestions.Add(item.GetString()!);
                }
            }

            if (root.TryGetProperty("recommendedTopicsToReview", out var rtProp) && rtProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rtProp.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(item.GetString()))
                        report.RecommendedTopicsToReview.Add(item.GetString()!);
                }
            }
        }
        catch
        {
            return CreateFallbackReport(session, null);
        }

        if (string.IsNullOrWhiteSpace(report.Summary))
        {
            return CreateFallbackReport(session, null);
        }

        return report;
    }

    private static TestEvaluationReport CreateFallbackReport(AiTestSession session, string? note)
    {
        var report = new TestEvaluationReport
        {
            Summary = $"You scored {session.ScorePercentage}% ({session.CorrectCount}/{session.TotalQuestions} correct) on {session.Topic}.",
            OverallAssessment = session.ScorePercentage >= 75
                ? "Excellent performance showing solid understanding of core concepts with high accuracy."
                : (session.ScorePercentage >= 50
                    ? "Good foundation with several correct answers, but further revision is recommended on missed concepts."
                    : "Foundational revision needed. Focus on core formulas, definitions, and problem-solving mechanisms.")
        };

        var correctSubtopics = session.Questions.Where(q => q.IsCorrect).Select(q => q.Subtopic).Distinct().ToList();
        var missedSubtopics = session.Questions.Where(q => !q.IsCorrect).Select(q => q.Subtopic).Distinct().ToList();

        report.StrongAreas = correctSubtopics.Count > 0 ? correctSubtopics : [$"{session.Topic} fundamentals"];
        report.WeakAreas = missedSubtopics.Count > 0 ? missedSubtopics : ["Revision of tricky sub-topics"];
        report.ActionableSuggestions =
        [
            $"Review the step-by-step explanations for the {session.IncorrectCount + session.UnansweredCount} missed questions.",
            "Create concise revision flashcards for key formulas and definitions in this unit.",
            "Retake a targeted practice session once concepts are refreshed."
        ];
        report.RecommendedTopicsToReview = missedSubtopics;

        return report;
    }

    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";

        // Try extracting from ```json ... ``` code fence
        var match = Regex.Match(text, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try extracting first { to last }
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1).Trim();
        }

        return text.Trim();
    }
}
