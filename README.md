# NextStep POS Reports – Cloud Backend

A serverless pipeline on AWS that receives aircraft **position reports (POS reports)** over HTTP, stores the raw message,
parses it into a structured record, calculates the remaining flight time and estimated fuel at arrival, and lets callers
check a flight's status at any time.

- **Lambdas:** C# / .NET 8 (arm64)
- **Infrastructure:** AWS CDK v2 in TypeScript, deployable with `cdk deploy`
- **AWS services:** API Gateway (HTTP API), Lambda, S3, DynamoDB, SQS

---

## Contents

1. [How it works](#how-it-works)
2. [API](#api)
3. [Repository layout](#repository-layout)
4. [Prerequisites](#prerequisites)
5. [Install](#install)
6. [Build and test locally](#build-and-test-locally)
7. [Deploy to AWS](#deploy-to-aws)
8. [Test on AWS](#test-on-aws)
9. [Logs](#logs)
10. [Design decisions and trade-offs](#design-decisions-and-trade-offs)
11. [Failure handling](#failure-handling)
12. [Assumptions](#assumptions)

---

## How it works

![Architecture](docs/architecture.png)

<sub>Editable source: [docs/architecture.svg](docs/architecture.svg)</sub>

| Step | Component | What happens |
|---|---|---|
| ① | **ReceivePosReport** Lambda (`POST /pos-reports`) | Validates the report (400 if malformed), builds the `flightId`, stores the raw text at `pos/{flightId}-{receivedAt}` and returns `{ flightId, status: "RECEIVED" }` immediately. |
| ② | S3 event notification | Fires for new objects **under `pos/` only** and invokes the parser. |
| ③ | **ParsePosReport** Lambda | Parses the report, writes the structured JSON to `attachment/{flightId}-{reportTimestamp}.json` and a record to the **ParsedReports** table. |
| ④ | **ParsePosReport** Lambda | Sends `{ flightId, timestamp }` to the **CalculationQueue**. |
| ⑤–⑨ | **CalculateFlightStatus** Lambda (SQS trigger) | Looks up the parsed record, reads its attachment, calculates distance (haversine), remaining time and fuel at arrival, writes the result to `results/{flightId}-{calculatedAt}.json` and a record to the **CalculationResults** table. |
| – | **GetFlightStatus** Lambda (`GET /status/{flightId}`) | Returns the latest calculation for the flight. If none exists yet it returns the latest parsed report (`PARSED`), otherwise 404. |

Because the pipeline is asynchronous, a status lookup right after submitting may return `PARSED` or 404 for a
moment. That is expected, not an error.

### Example

The report `POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800`, received at `2026-09-04T15:12:30.123Z`, produces:

| Artifact | Value |
|---|---|
| `flightId` | `UL20420260904RGNBKK` |
| Raw object | `pos/UL20420260904RGNBKK-2026-09-04T15:12:30.123Z` |
| Attachment | `attachment/UL20420260904RGNBKK-2026-09-04T12:05:00Z.json` (lat `16.705`, lon `96.2083`, …) |
| Result | ≈ 319 NM to BKK → **43 min** remaining, **10513 kg** fuel at arrival, no low-fuel warning |

---

## API

The base URL is the `ApiUrl` stack output (see [Deploy to AWS](#deploy-to-aws)). The API has no authentication and is
rate limited to 50 requests/second (burst 100); requests over the limit get `429 Too Many Requests`.

### `POST /pos-reports`

Body: the raw POS report as plain text.

```bash
curl -X POST "$API_URL/pos-reports" -H 'Content-Type: text/plain' \
  --data 'POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800'
```

| Status | Body |
|---|---|
| `202 Accepted` | `{"flightId":"UL20420260904RGNBKK","status":"RECEIVED"}` |
| `400 Bad Request` | `{"message":"The POS report must have 8 '/'-separated parts but has 3."}` |

### `GET /status/{flightId}`

```bash
curl "$API_URL/status/UL20420260904RGNBKK"
```

| Status | Body |
|---|---|
| `200` (calculated) | `{"flightId":"UL20420260904RGNBKK","status":"CALCULATED","timestamp":"2026-09-04T12:06:15.842Z","remainingFlightTimeMinutes":43,"estimatedFuelAtArrivalKg":10513,"lowFuelWarning":false}` |
| `200` (not calculated yet) | `{"flightId":"UL20420260904RGNBKK","status":"PARSED","timestamp":"2026-09-04T12:05:00Z"}` |
| `400` | `{"message":"'abc' is not a valid flight id, expected e.g. 'UL20420260904RGNBKK'."}` |
| `404` | `{"message":"No status found for flight 'UL20420260904RGNBKK'."}` |

`status` is one of `PARSED`, `CALCULATED` or `LOW_FUEL_WARNING` (estimated fuel at arrival is negative).
`timestamp` is the calculation time once calculated, otherwise the POS report time.

---

## Repository layout

```
├── src/
│   ├── NextStep.PosReports.Domain/          Pure business logic, no AWS dependencies
│   │   ├── Parsing/        PosReportParser, CoordinateParser
│   │   ├── Calculation/    GreatCircle (haversine), FlightEstimator
│   │   ├── Airports/       AirportDirectory (hardcoded code → coordinates)
│   │   ├── Identifiers/    FlightIdentifier, ObjectKeys (S3 key conventions)
│   │   ├── Models/         PosReport, FlightCalculationResult, FlightStatus
│   │   └── Time/           UtcTimestamp formats and JSON converters
│   ├── NextStep.PosReports.Infrastructure/  AWS adapters behind interfaces (S3, DynamoDB, SQS, API responses)
│   └── Lambdas/                             One project per function, each a thin handler
│       ├── NextStep.PosReports.Lambda.ReceivePosReport/
│       ├── NextStep.PosReports.Lambda.ParsePosReport/
│       ├── NextStep.PosReports.Lambda.CalculateFlightStatus/
│       └── NextStep.PosReports.Lambda.GetFlightStatus/
├── tests/
│   ├── NextStep.PosReports.Domain.Tests/    Parser, coordinates, haversine, keys, edge cases
│   └── NextStep.PosReports.Lambda.Tests/    Handlers with mocked adapters (NSubstitute)
├── aws/                                     CDK app (TypeScript)
│   ├── bin/pos-reports-app.ts               Entry point
│   ├── lib/pos-reports-stack.ts             The stack
│   ├── lib/constructs/dotnet-function.ts    .NET Lambda construct with a log-only base role
│   └── test/                                Template assertions (Jest)
├── integration/                             End-to-end tests against a deployed stack (Jest)
├── scripts/
│   ├── build-lambdas.sh                     dotnet publish → cdk.artifacts/ (run automatically by cdk synth/deploy)
│   └── smoke-test.sh                        Quick manual check of a deployment
├── docs/                                    Architecture diagram
├── NextStep.PosReports.CloudBackend.sln
├── Directory.Build.props / Directory.Packages.props   Shared .NET settings, central package versions
└── cdk.json, package.json, tsconfig.json, jest*.config.js
```

`cdk.artifacts/`, `cdk.out/` and `cdk.outputs/` are generated and git-ignored.

---

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| Git | any | `git --version` |
| .NET SDK | 8 or later (the Lambdas target .NET 8) | `dotnet --version` |
| Node.js | 22 or later | `node --version` |
| AWS CLI | v2 (only needed to deploy) | `aws --version` |
| AWS account | with permission to create the resources above (only needed to deploy) | – |

Docker is **not** required: the Lambdas are built with `dotnet publish`. The AWS CDK CLI doesn't need a global install;
`npm ci` installs it locally and it is run with `npx cdk`.

---

## Install

```bash
git clone https://github.com/vinay562/nextstep-pos-reports-cloud-backend.git
cd nextstep-pos-reports-cloud-backend
npm ci                    # CDK, TypeScript and test tooling
```

NuGet packages are restored automatically by the first `dotnet` build or test.

---

## Build and test locally

The pipeline itself runs on AWS. Locally you run the unit and infrastructure tests, which need no AWS account:

```bash
npm test                  # everything below: .NET (xUnit) + CDK (Jest)
```

Or separately:

```bash
dotnet test               # Domain logic (parsing, coordinates, haversine, keys) and Lambda handlers with mocks
npm run test:cdk          # CDK template assertions
npm run typecheck         # TypeScript type check
npx cdk synth             # build the Lambda packages and synthesize the CloudFormation template into cdk.out/
```

The CDK tests check, among other things, that the S3 notification is limited to `pos/`, that the queue has a DLQ,
that **each function's role contains exactly the expected list of actions and resources** (one extra permission fails
the build), and that **no IAM statement in the template uses a wildcard action or `Resource: "*"`**.

---

## Deploy to AWS

### 1. One-time setup per AWS account and region

```bash
aws configure sso         # or: aws login / aws configure, any way of getting CLI credentials
aws sts get-caller-identity
npx cdk bootstrap aws://<ACCOUNT_ID>/<REGION>
```

The stack deploys to the account and region of your active AWS CLI profile. To target another region, prefix the
commands with it, e.g. `AWS_REGION=eu-west-1 npx cdk deploy`.

### 2. Deploy

```bash
npx cdk deploy
```

`cdk deploy` builds the four Lambda packages (`scripts/build-lambdas.sh`, configured in `cdk.json`), deploys the
stack, and writes the outputs (`ApiUrl`, `BucketName`, table names, queue URLs) to `cdk.outputs/outputs.json`.
CDK lists the IAM changes and asks you to confirm them.

```bash
export API_URL=$(node -p "require('./cdk.outputs/outputs.json').NextStepPosReports.ApiUrl")
```

| Command | Purpose |
|---|---|
| `npx cdk diff` | Preview changes before deploying |
| `npx cdk destroy` | Remove the stack and **everything** in it, including stored reports |

---

## Logs

Each function logs JSON to its own CloudWatch log group (2-week retention). Find them via the Lambda console →
function → *Monitor* → *View CloudWatch logs*, or:

```bash
aws logs tail /aws/lambda/<function-name> --follow
```

Logs cover important events and errors only, never every step:

| Function | Information | Warning | Error |
|---|---|---|---|
| ReceivePosReport | report received and stored (flight id, key) | report rejected (reason) | storing the report failed |
| ParsePosReport | report parsed and queued (flight id, attachment key) | – | parsing failed (object key) |
| CalculateFlightStatus | flight calculated (minutes, fuel) | low fuel at arrival | calculation failed (message id, receive count, body) |
| GetFlightStatus | – (read endpoint polled by clients, so successful lookups aren't logged) | – | lookup failed (flight id) |

Errors are logged with the exception and then rethrown, so Lambda's error metrics and retries still apply.

---

## Test on AWS

Manually, with the requests from [API](#api). The report's day must exist in the current month, so use today's:

```bash
curl -X POST "$API_URL/pos-reports" -H 'Content-Type: text/plain' \
  --data "POS/UL204.FR RGN/TO BKK/$(date -u +%d)1205/N1642.3E09612.5/450/12500/2800"
curl "$API_URL/status/<flightId from the response>"
```

Automated:

```bash
npm run smoke-test        # submits the example report and polls the status until it is calculated
npm run test:integration  # full end-to-end suite (~5 minutes, see below)
```

The integration tests (`integration/`) read `cdk.outputs/outputs.json` and use your AWS credentials to check what
the pipeline stored. They cover:

- **Validation:** malformed reports and flight ids return 400 with a reason; an unknown flight returns 404.
- **Happy path:** the specification example through every stage (raw object, attachment, both tables, result
  object, status API) with the exact expected values.
- **Low fuel:** a report without enough fuel is marked `LOW_FUEL_WARNING`.
- **Several reports per flight:** the status is the most recent calculation.
- **Failure path:** a report the calculator cannot process ends up in the dead-letter queue, and the flight stays
  `PARSED`. This one waits for 3 delivery attempts × 90 s visibility timeout, which is why the suite takes ~5 minutes.
  Run just the fast suites with `npx jest -c jest.integration.config.js api-validation pipeline`.

Each run uses random flight numbers, so runs don't interfere with each other or with existing data.

---

## Design decisions and trade-offs

**Pure domain, thin handlers.** All parsing and calculation logic lives in `Domain` with no AWS dependencies and is
unit-tested in isolation. Handlers only orchestrate. AWS access goes through small interfaces (`IObjectStorage`,
`I…Repository`, `ICalculationRequestPublisher`), so handlers are tested with mocks and a `FakeTimeProvider`.

**One project per Lambda.** Each function ships only its own handler plus the shared libraries, can be deployed and
reasoned about independently, and gets its own IAM role.

**Full validation at the API.** The spec asks for a "roughly well-formed" check. The API runs the full parser
instead and returns a specific 400 reason. The cost is negligible, the caller gets immediate feedback, and malformed
reports never enter the pipeline. Both the API and the parser use the same parser code, so they can't disagree.

**The receive time drives the flight date.** The report only has day/hour/minute, so year and month come from the
moment the API received it. The parser takes that time **from the S3 key**, not from its own clock, so the `flightId`
it writes always matches the one the API returned, even if parsing happens after midnight at month end.

**Status reads only DynamoDB.** The calculation-results item carries the headline figures (`status`,
`remainingFlightTimeMinutes`, `estimatedFuelAtArrivalKg`, `lowFuelWarning`) in addition to the spec's
`flightId`/`timestamp`/`attachment`. The status endpoint is therefore two cheap queries (`ScanIndexForward=false, Limit=1`)
with no S3 reads, and its role needs no S3 permissions at all. The full self-contained result stays in S3.

**Least-privilege IAM, enforced by tests.** Every permission is an explicit statement scoped to one S3 prefix, table or
queue, and limited to the API calls the code actually makes:

| Function role | Allowed |
|---|---|
| ReceivePosReport | `s3:PutObject` on `pos/*` |
| ParsePosReport | `s3:GetObject` on `pos/*` · `s3:PutObject` on `attachment/*` · `dynamodb:PutItem` on ParsedReports · `sqs:SendMessage` on CalculationQueue and ParserDeadLetterQueue |
| CalculateFlightStatus | `dynamodb:GetItem` on ParsedReports · `s3:GetObject` on `attachment/*` · `s3:PutObject` on `results/*` · `dynamodb:PutItem` on CalculationResults · `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:GetQueueAttributes` on CalculationQueue |
| GetFlightStatus | `dynamodb:Query` on both tables (no S3 access) |

Every role also has `logs:CreateLogStream` and `logs:PutLogEvents` on **its own** log group only, and no managed
policies. The standard `AWSLambdaBasicExecutionRole` is not used because it allows writing to any log group.
The SQS trigger and the parser's failure destination are wired explicitly instead of with CDK's `SqsEventSource` /
`SqsDestination` helpers, because those add `sqs:GetQueueUrl` and `sqs:ChangeMessageVisibility`, which aren't needed.
Each function can only be invoked by its own trigger (the API route, or this bucket in this account).

*Exception:* the two helper Lambdas that CDK itself creates (one configures the bucket notification at deploy time,
one empties the bucket on `cdk destroy`) use AWS's `AWSLambdaBasicExecutionRole`, which lets them write logs to any
log group. They only run during deploy/destroy and aren't reachable from the API.

**Timestamps as sortable strings.** Report times use second precision (`2026-09-04T12:05:00Z`). System times
(receive, calculation) use millisecond precision (`…T15:12:30.123Z`), matching the spec. Each table only ever holds
one format, so lexical order is chronological order for the sort key.

**HTTP API instead of REST API.** It's cheaper and simpler, and covers both routes. Request validation is done in
the Lambda anyway.

**202 Accepted** for `POST /pos-reports`: the report is accepted for asynchronous processing, not yet processed.

**Rate limiting instead of authentication.** The spec defines no callers or auth, so the API is open. A stage-level
throttle (50 req/s, burst 100) protects the pipeline and the AWS bill from a flood. `GET /status` also checks the
flight id format before querying DynamoDB.

**Build without Docker.** `cdk synth`/`cdk deploy` first run `dotnet publish -r linux-arm64`, which produces the
packages directly; arm64 (Graviton) is cheaper for Lambda. The trade-off is that the build uses the local .NET SDK
instead of a container.

**Disposable by default.** The bucket and tables use `RemovalPolicy.DESTROY` (with auto-delete of objects) so
`cdk destroy` leaves nothing behind. For production this would be `RETAIN` plus point-in-time recovery and versioning.

**Next steps for production:** API authentication (IAM or JWT authorizer), CloudWatch alarms on dead-letter queue
depth, and X-Ray tracing.

---

## Failure handling

| Situation | Behaviour |
|---|---|
| Malformed report or flight id | `400` with a reason. Nothing is stored. |
| Too many requests | `429` from API Gateway (rate limit); the caller retries later. |
| AWS error in `POST /pos-reports` | The Lambda fails and the caller gets `500` and can safely resubmit. |
| Parser fails (e.g. S3/DynamoDB/SQS error) | S3 invokes the parser asynchronously. Lambda **retries twice**, then sends the event (with the error) to **ParserDeadLetterQueue** (14-day retention). Every parser step writes to deterministic keys, so retries are idempotent. |
| Calculator fails for one message | The handler reports a **partial batch failure**: only that message returns to the queue. It is retried after the 90 s visibility timeout (6× the 15 s function timeout). |
| Message keeps failing | After **3 receives** SQS moves it to the **Calculation DLQ** (14-day retention). Nothing is lost or retried forever. After fixing the cause, replay with `aws sqs start-message-move-task --source-arn <DLQ ARN>`. |
| Unknown destination airport | A permanent error: it fails all 3 attempts and lands in the Calculation DLQ. The flight stays `PARSED`, and an error is logged with the message body. |
| Duplicate delivery (S3 and SQS are at-least-once) | The parser overwrites the same attachment and record. The calculator may write one extra result row, and the status endpoint still returns the latest one. |

---

## Assumptions

- **Position format** follows the example and table in the spec: the hemisphere letter comes *before* the digits
  (`N1642.3E09612.5`). Latitude has 2 degree digits, longitude 3, and minutes are decimal and must be < 60.
- **Decimal degrees are rounded to 4 places** (≈ 11 m), which reproduces the spec's `96.2083`.
- **Year and month come from the receive time in UTC.** A report timed on the last day of a month but received after
  midnight UTC on the 1st gets the new month (and is rejected if that day doesn't exist in the new month). Inferring
  the previous month would need a heuristic the spec doesn't define.
- **Flight number:** 2-character IATA airline designator + 1–4 digits + optional letter suffix (e.g. `UL204`, `9W123`).
  Airport codes are 3 upper-case letters.
- **Ground speed** must be > 0 (it is a divisor). Fuel on board and fuel flow are whole kilograms ≥ 0.
- **Destination airports** are validated only by the calculator, against the hardcoded directory in
  `AirportDirectory.cs` (RGN, BKK, SIN, KUL, HKG, DEL, SYD, LHR, JFK). Add entries there to support more.
- **Rounding:** remaining minutes and fuel at arrival are rounded half away from zero. The low-fuel flag is based on
  the unrounded value.
- **"Most recent status"** means the latest calculation by calculation time. A flight shows `PARSED` only until its
  first calculation finishes.
