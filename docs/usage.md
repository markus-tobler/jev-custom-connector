# Using the TypeSafe Jev v1 connector

## Create a connection

Add the **TypeSafe Jev v1** connector and enter your TypeSafe API key. You can enter the key with or without a `Bearer ` prefix; the connector always sends `Authorization: Bearer <key>`. If the key is empty, the connector returns `401 missing_api_key` without calling TypeSafe.

## Evaluate with Jev

| Field | Notes |
| --- | --- |
| State | Text that Jev evaluates, such as an email, a ticket, or a transcript. If the whole text is a JSON object or array, the connector sends it as structured JSON so questions can refer to its fields (for example `` `order.total` ``). Decimal values such as `12.50` are kept as written. Any other text, including JSON numbers, booleans, and strings, is sent as text. |
| Model | Choose from the picker or enter a value. The default is `jev-latest`. Choose a pinned version, such as `jev-1.13.0`, if you need reproducible results. |
| Questions | One row per question (see below). |

### Question rows

Add one row for each question. The designer shows every field on every row; fill in only the fields for the row's type and leave the rest empty. In row JSON, a choice option's key is the `option` property, not `key`: the Power Automate array editor hides item properties named `key`.

| Field | noul | choice | score |
| --- | --- | --- | --- |
| Question ID | required, unique | required, unique | required, unique |
| Instructions | optional | optional | optional |
| Noul: yes / no means | describes the true and false answers; leave a field blank to send `null` | – | – |
| Choice options | – | 1–255 options, each with a unique **Option key** and an optional **Option description** | – |
| Score levels | – | – | 2–10 descriptions; level 0 comes first |

### Passing questions from an expression

Use **Switch to input entire array** on **Questions** to pass the questions from an expression or variable. The value can be:

- an array of rows in the same shape as above, for example `[{"id":"is_urgent","type":"noul"},{"id":"category","type":"choice","options":[{"option":"billing","description":"Payments"},{"option":"other"}]}]`, or
- the `questions` object in TypeSafe's native format. The connector validates it but forwards it unchanged, including any fields it does not recognize.

Either value can also be JSON text.

```json
{
  "is_urgent": { "type": "noul", "instructions": "Does this need an immediate response?", "criteria": { "true": "Production impact today.", "false": null } },
  "category": { "type": "choice", "criteria": { "billing": "Payments", "engineering": "Outages and bugs", "other": null } },
  "severity": { "type": "score", "criteria": ["No impact", "Minor", "Feature broken", "Outage"] }
}
```

The designer can't evaluate expressions, so with this input the action shows only the generic outputs (see below).

### Dynamic outputs in Power Automate

After you enter the questions, the designer calls `GetOutputSchema`, which is answered locally in the custom code, and builds tokens for each question:

| Question type | Tokens |
| --- | --- |
| noul | `<id> yes probability` (0–1) |
| choice | `<id> choice`, `<id> confidence`, `<id> probability <option>` |
| score | `<id> score` (expected value, can be fractional), `<id> confidence`, `<id> probability <n>`, `<id> legend <n>` |

The tokens `Model`, `Input tokens`, and `Output tokens` are always available. If the questions are incomplete, invalid, or come from an expression, the designer gets a generic schema in which **Answers** is a plain object. The action still runs, and the error is reported at run time.

Example condition: `@greater(body('Evaluate_with_Jev')?['answers']?['is_urgent']?['noul'], 0.8)`.

If you change the questions in a saved flow, edit the action again so the designer refreshes the schema.

## Power Apps

Canvas apps do not render dynamic schemas, so use **Evaluate with Jev (JSON text)** together with `ParseJSON`:

```powerfx
Set(
    jevResult,
    ParseJSON(
        'TypeSafeJevv1'.EvaluateJson({
            state: txtMessage.Text,
            questions: Table(
                { id: "is_urgent", type: "noul", noulTrue: "Needs a reply today" },
                { id: "category", type: "choice", options: Table({ option: "billing", description: "Payments" }, { option: "other" }) }
            )
        }).responseJson
    )
);
Value(jevResult.answers.is_urgent.noul)
```

Use `Text(jevResult.answers.category.choice)` for choice answers. In Power Apps, the connector's name is its display name with spaces and punctuation removed; IntelliSense shows the exact name.

## List models

**List models** returns `models[]` with `name`, `description`, and `release_date`.

## Errors

The connector returns validation errors as HTTP 400 with this body:

```json
{ "error": { "code": "duplicate_question_id", "message": "...", "path": "questions[1].id" } }
```

Common codes:

- `missing_state`, `invalid_state`, `invalid_model`
- `missing_questions`, `invalid_questions`, `invalid_questions_json`, `missing_question_id`, `duplicate_question_id`, `invalid_question_type`
- `invalid_noul_criteria`, `missing_choice_options`, `too_many_choice_options`, `missing_choice_key`, `duplicate_choice_key`, `invalid_score_levels`

TypeSafe errors are passed through unchanged:

| Status | Meaning |
| --- | --- |
| 401 | Invalid API key. |
| 422 | TypeSafe rejected the request. |
| 429 | Rate limited. Honor `retry-after`. |
| 529 | Overloaded. Honor `retry-after`. |

The connector never retries. Configure a retry policy on the action, or build your own retry logic, if you need it.
