public class Script : ScriptBase
{
    private const string EvaluateOperation = "Evaluate";
    private const string EvaluateJsonOperation = "EvaluateJson";
    private const string GetOutputSchemaOperation = "GetOutputSchema";
    private const string ListModelsOperation = "ListModels";
    private const string GetModelChoicesOperation = "GetModelChoices";

    private const string SystemOnePath = "/v1/systemone";
    private const string ModelsPath = "/v1/models";
    private const string DefaultModel = "jev-latest";
    private const string BearerPrefix = "Bearer ";
    private const string RequestIdHeader = "x-typesafe-request-id";

    private const int MaxChoiceOptions = 255;
    private const int MinScoreLevels = 2;
    private const int MaxScoreLevels = 10;

    private static readonly string[] KnownOperations =
    {
        EvaluateOperation,
        EvaluateJsonOperation,
        GetOutputSchemaOperation,
        ListModelsOperation,
        GetModelChoicesOperation
    };

    private static readonly string[][] ModelAliases =
    {
        new[] { "jev-latest", "jev-latest (latest stable release)" },
        new[] { "jev-preview", "jev-preview (preview release)" }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = ResolveOperationId(this.Context.OperationId);
        this.Context.Logger.LogInformation("Jev connector operation {operationId}", operationId ?? "unknown");

        try
        {
            switch (operationId)
            {
                case EvaluateOperation:
                    return await this.EvaluateAsync(false).ConfigureAwait(false);
                case EvaluateJsonOperation:
                    return await this.EvaluateAsync(true).ConfigureAwait(false);
                case GetOutputSchemaOperation:
                    return await this.GetOutputSchemaAsync().ConfigureAwait(false);
                case ListModelsOperation:
                    return await this.SendToTypeSafeAsync(HttpMethod.Get, ModelsPath, null).ConfigureAwait(false);
                case GetModelChoicesOperation:
                    return await this.GetModelChoicesAsync().ConfigureAwait(false);
                default:
                    throw new ConnectorException(HttpStatusCode.BadRequest, "unknown_operation", "The connector does not support this operation.", null);
            }
        }
        catch (ConnectorException ex)
        {
            this.Context.Logger.LogWarning("Jev connector rejected request: {code} at {path}", ex.Code, ex.Path ?? string.Empty);
            return CreateError(ex.StatusCode, ex.Code, ex.Message, ex.Path);
        }
    }

    private async Task<HttpResponseMessage> EvaluateAsync(bool wrapAsJson)
    {
        var body = await this.ReadJsonBodyAsync().ConfigureAwait(false);
        var payload = BuildSystemOnePayload(body);

        var upstream = await this.SendToTypeSafeAsync(HttpMethod.Post, SystemOnePath, payload.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
        this.Context.Logger.LogInformation("TypeSafe evaluation returned {status}", (int)upstream.StatusCode);

        if (!wrapAsJson || !upstream.IsSuccessStatusCode)
        {
            return upstream;
        }

        var responseText = upstream.Content == null
            ? string.Empty
            : await upstream.Content.ReadAsStringAsync().ConfigureAwait(false);

        var wrapped = new HttpResponseMessage(upstream.StatusCode)
        {
            Content = CreateJsonContent(new JObject { ["responseJson"] = responseText }.ToString(Newtonsoft.Json.Formatting.None))
        };

        IEnumerable<string> requestIds;
        if (upstream.Headers.TryGetValues(RequestIdHeader, out requestIds))
        {
            wrapped.Headers.TryAddWithoutValidation(RequestIdHeader, requestIds);
        }

        return wrapped;
    }

    private async Task<HttpResponseMessage> GetOutputSchemaAsync()
    {
        JObject schema;
        try
        {
            var body = await this.ReadJsonBodyAsync().ConfigureAwait(false);
            schema = BuildResponseSchema(NormalizeQuestions(body["questions"], "questions"));
        }
        catch (ConnectorException ex)
        {
            // Design-time callers get a usable generic schema instead of a blocking error; the
            // same questions are rejected with details when the action actually runs.
            schema = BuildGenericResponseSchema(ex.Message);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(new JObject { ["schema"] = schema }.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private async Task<HttpResponseMessage> GetModelChoicesAsync()
    {
        var upstream = await this.SendToTypeSafeAsync(HttpMethod.Get, ModelsPath, null).ConfigureAwait(false);
        if (!upstream.IsSuccessStatusCode)
        {
            return upstream;
        }

        var choices = new JArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in ModelAliases)
        {
            seen.Add(alias[0]);
            choices.Add(new JObject { ["name"] = alias[0], ["title"] = alias[1] });
        }

        var text = upstream.Content == null ? string.Empty : await upstream.Content.ReadAsStringAsync().ConfigureAwait(false);
        JToken parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(text) ? null : ParseJson(text);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        var models = parsed is JObject ? parsed["models"] as JArray : null;
        if (models != null)
        {
            foreach (var model in models.OfType<JObject>())
            {
                var nameToken = model["name"];
                var name = nameToken != null && nameToken.Type == JTokenType.String ? (string)nameToken : null;
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    continue;
                }

                var releaseToken = model["release_date"];
                var releaseDate = releaseToken != null && releaseToken.Type == JTokenType.String ? (string)releaseToken : null;
                var title = string.IsNullOrWhiteSpace(releaseDate) ? name : name + " (" + releaseDate + ")";
                choices.Add(new JObject { ["name"] = name, ["title"] = title });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(new JObject { ["models"] = choices }.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private async Task<JObject> ReadJsonBodyAsync()
    {
        var content = this.Context.Request.Content;
        var text = content == null ? string.Empty : await content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_body", "The request body is required.", null);
        }

        JToken token;
        try
        {
            token = ParseJson(text);
        }
        catch (JsonException)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_body", "The request body must be a JSON object.", null);
        }

        var body = token as JObject;
        if (body == null)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_body", "The request body must be a JSON object.", null);
        }

        return body;
    }

    private async Task<HttpResponseMessage> SendToTypeSafeAsync(HttpMethod method, string path, string jsonContent)
    {
        var original = this.Context.Request;
        var authorization = NormalizeAuthorization(original);

        var builder = new UriBuilder(original.RequestUri) { Path = path, Query = string.Empty };
        var request = new HttpRequestMessage(method, builder.Uri);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonContent != null)
        {
            request.Content = CreateJsonContent(jsonContent);
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    internal static string ResolveOperationId(string operationId)
    {
        if (string.IsNullOrEmpty(operationId) || KnownOperations.Contains(operationId))
        {
            return operationId;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(operationId));
            if (KnownOperations.Contains(decoded))
            {
                return decoded;
            }
        }
        catch (FormatException)
        {
        }

        return operationId;
    }

    internal static string NormalizeAuthorization(HttpRequestMessage request)
    {
        IEnumerable<string> values;
        var raw = request.Headers.TryGetValues("Authorization", out values) ? values.FirstOrDefault() : null;
        raw = raw == null ? string.Empty : raw.Trim();

        if (string.Equals(raw, BearerPrefix.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            raw = string.Empty;
        }
        else if (raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(BearerPrefix.Length).Trim();
        }

        if (raw.Length == 0)
        {
            throw new ConnectorException(HttpStatusCode.Unauthorized, "missing_api_key", "The connection does not provide a TypeSafe API key.", null);
        }

        return BearerPrefix + raw;
    }

    internal static JObject BuildSystemOnePayload(JObject body)
    {
        var payload = new JObject();
        payload["state"] = NormalizeState(body);

        var model = body["model"];
        if (model != null && model.Type != JTokenType.Null && model.Type != JTokenType.String)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_model", "The model must be text.", "model");
        }

        var modelName = model != null && model.Type == JTokenType.String ? ((string)model).Trim() : null;
        payload["model"] = string.IsNullOrEmpty(modelName) ? DefaultModel : modelName;
        payload["questions"] = NormalizeQuestions(body["questions"], "questions");
        return payload;
    }

    private static JToken NormalizeState(JObject body)
    {
        JToken state;
        if (!body.TryGetValue("state", out state))
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_state", "The state to evaluate is required.", "state");
        }

        if (!IsEntryValue(state))
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_state", "The state must be text, a JSON object, a JSON array, or null.", "state");
        }

        if (state.Type == JTokenType.String)
        {
            var structured = TryParseStructuredJson((string)state);
            if (structured != null)
            {
                return structured;
            }
        }

        return state.DeepClone();
    }

    // Text that is a complete JSON object or array is sent as structured JSON so questions can
    // refer to its fields; anything else, including JSON scalars, stays plain text.
    private static JToken TryParseStructuredJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            var token = ParseJson(trimmed);
            return token.Type == JTokenType.Object || token.Type == JTokenType.Array ? token : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static JObject NormalizeQuestions(JToken questions, string path)
    {
        if (questions == null || questions.Type == JTokenType.Null || (questions.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)questions)))
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_questions", "Add at least one question.", path);
        }

        if (questions.Type == JTokenType.String)
        {
            try
            {
                questions = ParseJson((string)questions);
            }
            catch (JsonException)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_questions_json", "The questions text is not valid JSON.", path);
            }
        }

        if (questions.Type == JTokenType.Array)
        {
            return ValidateNativeQuestions(BuildQuestionsFromRows((JArray)questions, path), path);
        }

        if (questions.Type == JTokenType.Object)
        {
            return ValidateNativeQuestions((JObject)questions, path);
        }

        throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_questions", "Questions must be a list of question rows or a TypeSafe questions object keyed by question ID.", path);
    }

    private static JObject BuildQuestionsFromRows(JArray rows, string path)
    {
        if (rows.Count == 0)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_questions", "Add at least one question.", path);
        }

        var questions = new JObject();
        for (var index = 0; index < rows.Count; index++)
        {
            var rowPath = path + "[" + index + "]";
            var row = rows[index] as JObject;
            if (row == null)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_question", "Each question must be an object.", rowPath);
            }

            var id = ReadOptionalString(row["id"], rowPath + ".id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "missing_question_id", "Each question needs an ID.", rowPath + ".id");
            }

            if (questions.Property(id) != null)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "duplicate_question_id", "Question ID '" + id + "' is used more than once.", rowPath + ".id");
            }

            var type = (ReadOptionalString(row["type"], rowPath + ".type") ?? string.Empty).Trim().ToLowerInvariant();
            var question = new JObject { ["type"] = type };

            var instructions = ReadOptionalString(row["instructions"], rowPath + ".instructions");
            if (!string.IsNullOrEmpty(instructions))
            {
                question["instructions"] = instructions;
            }

            switch (type)
            {
                case "noul":
                    var whenTrue = ReadOptionalString(row["noulTrue"], rowPath + ".noulTrue");
                    var whenFalse = ReadOptionalString(row["noulFalse"], rowPath + ".noulFalse");
                    if (!string.IsNullOrEmpty(whenTrue) || !string.IsNullOrEmpty(whenFalse))
                    {
                        question["criteria"] = new JObject
                        {
                            ["true"] = string.IsNullOrEmpty(whenTrue) ? JValue.CreateNull() : new JValue(whenTrue),
                            ["false"] = string.IsNullOrEmpty(whenFalse) ? JValue.CreateNull() : new JValue(whenFalse)
                        };
                    }

                    break;
                case "choice":
                    question["criteria"] = BuildChoiceCriteria(row["options"], rowPath + ".options");
                    break;
                case "score":
                    question["criteria"] = BuildScoreCriteria(row["levels"], rowPath + ".levels");
                    break;
                default:
                    throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_question_type", "The question type must be noul, choice, or score.", rowPath + ".type");
            }

            questions[id] = question;
        }

        return questions;
    }

    private static JObject BuildChoiceCriteria(JToken token, string path)
    {
        var options = token as JArray;
        if (options == null || options.Count == 0)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_choice_options", "A choice question needs at least one option.", path);
        }

        if (options.Count > MaxChoiceOptions)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "too_many_choice_options", "A choice question supports at most " + MaxChoiceOptions + " options.", path);
        }

        var criteria = new JObject();
        for (var index = 0; index < options.Count; index++)
        {
            var optionPath = path + "[" + index + "]";
            var option = options[index] as JObject;
            if (option == null)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_choice_option", "Each choice option must be an object with an option key and an optional description.", optionPath);
            }

            // The row property is "option", not "key": the Power Automate array editor hides item properties named "key".
            var key = ReadOptionalString(option["option"], optionPath + ".option");
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "missing_choice_key", "Each choice option needs a key.", optionPath + ".option");
            }

            if (criteria.Property(key) != null)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "duplicate_choice_key", "Choice option '" + key + "' is used more than once.", optionPath + ".option");
            }

            var description = ReadOptionalString(option["description"], optionPath + ".description");
            criteria[key] = string.IsNullOrEmpty(description) ? JValue.CreateNull() : new JValue(description);
        }

        return criteria;
    }

    private static JArray BuildScoreCriteria(JToken token, string path)
    {
        var levels = token as JArray;
        if (levels == null || levels.Count < MinScoreLevels || levels.Count > MaxScoreLevels)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_score_levels", "A score question needs between " + MinScoreLevels + " and " + MaxScoreLevels + " levels.", path);
        }

        var criteria = new JArray();
        for (var index = 0; index < levels.Count; index++)
        {
            var text = ReadOptionalString(levels[index], path + "[" + index + "]");
            criteria.Add(string.IsNullOrEmpty(text) ? JValue.CreateNull() : new JValue(text));
        }

        return criteria;
    }

    internal static JObject ValidateNativeQuestions(JObject questions, string path)
    {
        if (questions.Count == 0)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_questions", "Add at least one question.", path);
        }

        var normalized = new JObject();
        foreach (var property in questions.Properties())
        {
            var questionPath = path + "['" + property.Name + "']";
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "missing_question_id", "Question IDs must not be empty.", questionPath);
            }

            var question = property.Value as JObject;
            if (question == null)
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_question", "Each question must be an object.", questionPath);
            }

            JToken instructions;
            if (question.TryGetValue("instructions", out instructions) && !IsEntryValue(instructions))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_instructions", "Instructions must be text, a JSON object, a JSON array, or null.", questionPath + ".instructions");
            }

            var typeToken = question["type"];
            var type = typeToken != null && typeToken.Type == JTokenType.String ? (string)typeToken : null;
            var criteria = question["criteria"];
            switch (type)
            {
                case "noul":
                    ValidateNoulCriteria(criteria, questionPath + ".criteria");
                    break;
                case "choice":
                    ValidateChoiceCriteria(criteria, questionPath + ".criteria");
                    break;
                case "score":
                    ValidateScoreCriteria(criteria, questionPath + ".criteria");
                    break;
                default:
                    throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_question_type", "The question type must be noul, choice, or score.", questionPath + ".type");
            }

            normalized[property.Name] = question.DeepClone();
        }

        return normalized;
    }

    private static void ValidateNoulCriteria(JToken criteria, string path)
    {
        if (criteria == null || criteria.Type == JTokenType.Null)
        {
            return;
        }

        var criteriaObject = criteria as JObject;
        if (criteriaObject == null)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_noul_criteria", "Noul criteria must be an object with true and false descriptions.", path);
        }

        foreach (var property in criteriaObject.Properties())
        {
            if (property.Name != "true" && property.Name != "false")
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_noul_criteria", "Noul criteria only support the keys true and false.", path + "." + property.Name);
            }

            if (!IsEntryValue(property.Value))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_criteria_description", "Criteria descriptions must be text, a JSON object, a JSON array, or null.", path + "." + property.Name);
            }
        }
    }

    private static void ValidateChoiceCriteria(JToken criteria, string path)
    {
        var criteriaObject = criteria as JObject;
        if (criteriaObject == null || criteriaObject.Count == 0)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "missing_choice_options", "A choice question needs at least one option.", path);
        }

        if (criteriaObject.Count > MaxChoiceOptions)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "too_many_choice_options", "A choice question supports at most " + MaxChoiceOptions + " options.", path);
        }

        foreach (var property in criteriaObject.Properties())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "missing_choice_key", "Choice option keys must not be empty.", path);
            }

            if (!IsEntryValue(property.Value))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_criteria_description", "Criteria descriptions must be text, a JSON object, a JSON array, or null.", path + "['" + property.Name + "']");
            }
        }
    }

    private static void ValidateScoreCriteria(JToken criteria, string path)
    {
        var levels = criteria as JArray;
        if (levels == null || levels.Count < MinScoreLevels || levels.Count > MaxScoreLevels)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_score_levels", "A score question needs between " + MinScoreLevels + " and " + MaxScoreLevels + " levels.", path);
        }

        for (var index = 0; index < levels.Count; index++)
        {
            if (!IsEntryValue(levels[index]))
            {
                throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_criteria_description", "Criteria descriptions must be text, a JSON object, a JSON array, or null.", path + "[" + index + "]");
            }
        }
    }

    internal static JObject BuildResponseSchema(JObject questions)
    {
        var answers = new JObject();
        foreach (var property in questions.Properties())
        {
            answers[property.Name] = BuildAnswerSchema(property.Name, (JObject)property.Value);
        }

        return BuildEnvelopeSchema(new JObject
        {
            ["type"] = "object",
            ["description"] = "Answers keyed by question ID.",
            ["properties"] = answers
        });
    }

    internal static JObject BuildGenericResponseSchema(string reason)
    {
        var description = "Answers keyed by question ID. Named outputs appear once the questions are valid";
        description += string.IsNullOrEmpty(reason) ? "." : ": " + reason;
        return BuildEnvelopeSchema(new JObject
        {
            ["type"] = "object",
            ["description"] = description
        });
    }

    // The designer names each output token by joining the x-ms-summary of every enclosing object
    // with the token's own summary. Containers therefore carry no summary (answers, usage) or only
    // their own segment (question ID, probability, legend), and leaves carry only the last segment,
    // which yields names such as "category probability billing" without repeated words.
    private static JObject BuildEnvelopeSchema(JObject answers)
    {
        return new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["model"] = Property("string", "Model", "The model that answered the request."),
                ["answers"] = answers,
                ["usage"] = ObjectSchema(null, "Token usage for the request.", new JObject
                {
                    ["input_tokens"] = Property("integer", "Input tokens", "Number of input tokens used."),
                    ["output_tokens"] = Property("integer", "Output tokens", "Number of output tokens used.")
                })
            }
        };
    }

    private static JObject BuildAnswerSchema(string id, JObject question)
    {
        var type = (string)question["type"];
        var properties = new JObject
        {
            ["type"] = Property("string", "type", "The answer type.")
        };

        if (type == "noul")
        {
            properties["noul"] = Property("number", "yes probability", "Probability of a yes answer, from 0 to 1.");
        }
        else if (type == "choice")
        {
            var probabilities = new JObject();
            foreach (var option in ((JObject)question["criteria"]).Properties())
            {
                probabilities[option.Name] = Property("number", option.Name, "Probability of option " + option.Name + ".");
            }

            properties["choice"] = Property("string", "choice", "The selected option key.");
            properties["confidence"] = Property("number", "confidence", "Reported confidence in the selected option.");
            properties["probabilities"] = ObjectSchema("probability", "Probabilities keyed by option.", probabilities);
        }
        else
        {
            var criteria = (JArray)question["criteria"];
            var legend = new JObject();
            var probabilities = new JObject();
            for (var index = 0; index < criteria.Count; index++)
            {
                var key = Convert.ToString(index);
                var legendSchema = new JObject
                {
                    ["x-ms-summary"] = key,
                    ["description"] = "Rubric description for score " + key + "."
                };
                if (criteria[index].Type == JTokenType.String)
                {
                    legendSchema["type"] = "string";
                }

                legend[key] = legendSchema;
                probabilities[key] = Property("number", key, "Probability of score " + key + ".");
            }

            properties["score"] = Property("number", "score", "Expected score, which may fall between rubric levels.");
            properties["confidence"] = Property("number", "confidence", "Reported confidence in the score.");
            properties["legend"] = ObjectSchema("legend", "Rubric descriptions keyed by score.", legend);
            properties["probabilities"] = ObjectSchema("probability", "Probabilities keyed by score.", probabilities);
        }

        return ObjectSchema(id, "Answer for question " + id + ".", properties);
    }

    private static JObject ObjectSchema(string summary, string description, JObject properties)
    {
        var schema = new JObject { ["type"] = "object" };
        if (summary != null)
        {
            schema["x-ms-summary"] = summary;
        }

        schema["description"] = description;
        schema["properties"] = properties;
        return schema;
    }

    private static JObject Property(string type, string summary, string description)
    {
        var schema = new JObject
        {
            ["type"] = type,
            ["x-ms-summary"] = summary,
            ["description"] = description
        };
        if (type == "number")
        {
            schema["format"] = "double";
        }
        else if (type == "integer")
        {
            schema["format"] = "int64";
        }

        return schema;
    }

    private static string ReadOptionalString(JToken token, string path)
    {
        if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return null;
        }

        if (token.Type != JTokenType.String)
        {
            throw new ConnectorException(HttpStatusCode.BadRequest, "invalid_value", "The value must be text.", path);
        }

        return (string)token;
    }

    private static bool IsEntryValue(JToken token)
    {
        return token == null
            || token.Type == JTokenType.Null
            || token.Type == JTokenType.String
            || token.Type == JTokenType.Object
            || token.Type == JTokenType.Array;
    }

    private static JToken ParseJson(string text)
    {
        using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })
        {
            var token = JToken.ReadFrom(reader);
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.Comment)
                {
                    throw new JsonReaderException("Unexpected content after the JSON value.");
                }
            }

            return token;
        }
    }

    private static HttpResponseMessage CreateError(HttpStatusCode status, string code, string message, string path)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrEmpty(path))
        {
            error["path"] = path;
        }

        return new HttpResponseMessage(status)
        {
            Content = CreateJsonContent(new JObject { ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    internal class ConnectorException : Exception
    {
        public ConnectorException(HttpStatusCode statusCode, string code, string message, string path)
            : base(message)
        {
            this.StatusCode = statusCode;
            this.Code = code;
            this.Path = path;
        }

        public HttpStatusCode StatusCode { get; private set; }

        public string Code { get; private set; }

        public string Path { get; private set; }
    }
}
