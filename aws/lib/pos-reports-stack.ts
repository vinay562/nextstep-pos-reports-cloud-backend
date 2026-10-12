import * as path from 'node:path';
import * as cdk from 'aws-cdk-lib/core';
import * as apigwv2 from 'aws-cdk-lib/aws-apigatewayv2';
import { HttpLambdaIntegration } from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3Notifications from 'aws-cdk-lib/aws-s3-notifications';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';
import { DotnetFunction } from './constructs/dotnet-function';

/** S3 key prefixes; must match NextStep.PosReports.Domain.Identifiers.ObjectKeys. */
const OBJECT_PREFIX = {
  rawReports: 'pos/',
  attachments: 'attachment/',
  calculationResults: 'results/',
} as const;

const DEFAULT_LAMBDA_ARTIFACTS_PATH = path.join(__dirname, '..', '..', 'cdk.artifacts');

const CALCULATOR_TIMEOUT = cdk.Duration.seconds(15);
const CALCULATION_MAX_RECEIVE_COUNT = 3;
const PARSER_ASYNC_RETRY_ATTEMPTS = 2;
const DEAD_LETTER_RETENTION = cdk.Duration.days(14);
const API_RATE_LIMIT_PER_SECOND = 50;
const API_BURST_LIMIT = 100;

export interface PosReportsStackProps extends cdk.StackProps {
  /** Published Lambda packages (output of scripts/build-lambdas.sh). Defaults to <repo>/cdk.artifacts. */
  readonly lambdaArtifactsPath?: string;
}

/**
 * POS report pipeline:
 * API (POST) → S3 pos/ → Parser → S3 attachment/ + DynamoDB + SQS → Calculator → S3 results/ + DynamoDB ← API (GET)
 */
export class PosReportsStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: PosReportsStackProps = {}) {
    super(scope, id, props);

    const artifactsPath = props.lambdaArtifactsPath ?? DEFAULT_LAMBDA_ARTIFACTS_PATH;

    // ---------------------------------------------------------------- Storage

    const bucket = new s3.Bucket(this, 'PosReportsBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
      // Disposable stack: `cdk destroy` removes everything, including stored reports.
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    const parsedReportsTable = this.createFlightTable('ParsedReportsTable');
    const calculationResultsTable = this.createFlightTable('CalculationResultsTable');

    // -------------------------------------------------------------- Messaging

    const calculationDeadLetterQueue = new sqs.Queue(this, 'CalculationDeadLetterQueue', {
      retentionPeriod: DEAD_LETTER_RETENTION,
      enforceSSL: true,
    });

    const calculationQueue = new sqs.Queue(this, 'CalculationQueue', {
      // AWS guidance: at least 6x the consumer's timeout, so in-flight messages are not redelivered early.
      visibilityTimeout: cdk.Duration.seconds(CALCULATOR_TIMEOUT.toSeconds() * 6),
      deadLetterQueue: { queue: calculationDeadLetterQueue, maxReceiveCount: CALCULATION_MAX_RECEIVE_COUNT },
      enforceSSL: true,
    });

    // The parser is invoked asynchronously by S3; events that still fail after Lambda's retries land here.
    const parserDeadLetterQueue = new sqs.Queue(this, 'ParserDeadLetterQueue', {
      retentionPeriod: DEAD_LETTER_RETENTION,
      enforceSSL: true,
    });

    // -------------------------------------------------------------- Functions

    const receiveFunction = new DotnetFunction(this, 'ReceivePosReport', {
      projectName: 'NextStep.PosReports.Lambda.ReceivePosReport',
      description: 'POST /pos-reports: validates and stores the raw POS report',
      artifactsPath,
      timeout: cdk.Duration.seconds(10),
      environment: { BUCKET_NAME: bucket.bucketName },
    });

    const parseFunction = new DotnetFunction(this, 'ParsePosReport', {
      projectName: 'NextStep.PosReports.Lambda.ParsePosReport',
      description: 'Parses raw POS reports from pos/ and queues them for calculation',
      artifactsPath,
      timeout: cdk.Duration.seconds(30),
      environment: {
        BUCKET_NAME: bucket.bucketName,
        PARSED_REPORTS_TABLE_NAME: parsedReportsTable.tableName,
        CALCULATION_QUEUE_URL: calculationQueue.queueUrl,
      },
    });

    const calculateFunction = new DotnetFunction(this, 'CalculateFlightStatus', {
      projectName: 'NextStep.PosReports.Lambda.CalculateFlightStatus',
      description: 'Calculates remaining flight time and fuel at arrival for parsed POS reports',
      artifactsPath,
      timeout: CALCULATOR_TIMEOUT,
      environment: {
        BUCKET_NAME: bucket.bucketName,
        PARSED_REPORTS_TABLE_NAME: parsedReportsTable.tableName,
        CALCULATION_RESULTS_TABLE_NAME: calculationResultsTable.tableName,
      },
    });

    const statusFunction = new DotnetFunction(this, 'GetFlightStatus', {
      projectName: 'NextStep.PosReports.Lambda.GetFlightStatus',
      description: 'GET /status/{flightId}: returns the latest status of a flight',
      artifactsPath,
      timeout: cdk.Duration.seconds(10),
      environment: {
        PARSED_REPORTS_TABLE_NAME: parsedReportsTable.tableName,
        CALCULATION_RESULTS_TABLE_NAME: calculationResultsTable.tableName,
      },
    });

    // ------------------------------------------- Permissions (least privilege)
    // Each function gets only the actions it calls, on only the tables, queue and S3 prefixes it touches.

    const objectsUnder = (prefix: string) => bucket.arnForObjects(`${prefix}*`);

    receiveFunction.allow(['s3:PutObject'], [objectsUnder(OBJECT_PREFIX.rawReports)]);

    parseFunction.allow(['s3:GetObject'], [objectsUnder(OBJECT_PREFIX.rawReports)]);
    parseFunction.allow(['s3:PutObject'], [objectsUnder(OBJECT_PREFIX.attachments)]);
    parseFunction.allow(['dynamodb:PutItem'], [parsedReportsTable.tableArn]);
    parseFunction.allow(['sqs:SendMessage'], [calculationQueue.queueArn]);
    // Lambda delivers failed async invocations to the dead-letter queue using the function's role.
    parseFunction.allow(['sqs:SendMessage'], [parserDeadLetterQueue.queueArn]);

    calculateFunction.allow(['dynamodb:GetItem'], [parsedReportsTable.tableArn]);
    calculateFunction.allow(['s3:GetObject'], [objectsUnder(OBJECT_PREFIX.attachments)]);
    calculateFunction.allow(['s3:PutObject'], [objectsUnder(OBJECT_PREFIX.calculationResults)]);
    calculateFunction.allow(['dynamodb:PutItem'], [calculationResultsTable.tableArn]);
    // The minimum the SQS event source mapping needs (same as AWS's AWSLambdaSQSQueueExecutionRole).
    calculateFunction.allow(
      ['sqs:ReceiveMessage', 'sqs:DeleteMessage', 'sqs:GetQueueAttributes'],
      [calculationQueue.queueArn],
    );

    statusFunction.allow(['dynamodb:Query'], [parsedReportsTable.tableArn, calculationResultsTable.tableArn]);

    // --------------------------------------------------------------- Triggers

    bucket.addEventNotification(
      s3.EventType.OBJECT_CREATED,
      new s3Notifications.LambdaDestination(parseFunction.function),
      { prefix: OBJECT_PREFIX.rawReports },
    );

    // Wired explicitly rather than with SqsDestination / SqsEventSource: those helpers also grant
    // sqs:GetQueueUrl and sqs:ChangeMessageVisibility, which are not needed. Permissions are granted above.
    parseFunction.function.configureAsyncInvoke({
      retryAttempts: PARSER_ASYNC_RETRY_ATTEMPTS,
      onFailure: { bind: () => ({ destination: parserDeadLetterQueue.queueArn }) },
    });

    // Same construct ID that SqsEventSource uses, so the logical ID stays stable and CloudFormation updates the mapping
    // in place. Lambda allows only one mapping per queue and function, so a replacement (create-then-delete) would fail.
    calculateFunction.function.addEventSourceMapping(`SqsEventSource:${cdk.Names.nodeUniqueId(calculationQueue.node)}`, {
      eventSourceArn: calculationQueue.queueArn,
      batchSize: 10,
      reportBatchItemFailures: true,
    });

    // -------------------------------------------------------------------- API

    const httpApi = new apigwv2.HttpApi(this, 'PosReportsApi', {
      description: 'Receives POS reports and serves flight status',
    });

    // The API has no authentication, so cap the request rate to protect the pipeline and the bill from a flood.
    // Set on the existing default stage (not a new stage) so CloudFormation updates it in place.
    const defaultStage = httpApi.defaultStage!.node.defaultChild as apigwv2.CfnStage;
    defaultStage.defaultRouteSettings = {
      throttlingRateLimit: API_RATE_LIMIT_PER_SECOND,
      throttlingBurstLimit: API_BURST_LIMIT,
    };

    httpApi.addRoutes({
      path: '/pos-reports',
      methods: [apigwv2.HttpMethod.POST],
      integration: new HttpLambdaIntegration('ReceivePosReportIntegration', receiveFunction.function),
    });

    httpApi.addRoutes({
      path: '/status/{flightId}',
      methods: [apigwv2.HttpMethod.GET],
      integration: new HttpLambdaIntegration('GetFlightStatusIntegration', statusFunction.function),
    });

    // ---------------------------------------------------------------- Outputs
    // Written to cdk.outputs/outputs.json on deploy; the integration tests read them from there.

    new cdk.CfnOutput(this, 'Region', { value: this.region });
    new cdk.CfnOutput(this, 'ApiUrl', { value: httpApi.apiEndpoint });
    new cdk.CfnOutput(this, 'BucketName', { value: bucket.bucketName });
    new cdk.CfnOutput(this, 'ParsedReportsTableName', { value: parsedReportsTable.tableName });
    new cdk.CfnOutput(this, 'CalculationResultsTableName', { value: calculationResultsTable.tableName });
    new cdk.CfnOutput(this, 'CalculationQueueUrl', { value: calculationQueue.queueUrl });
    new cdk.CfnOutput(this, 'CalculationDeadLetterQueueUrl', { value: calculationDeadLetterQueue.queueUrl });
    new cdk.CfnOutput(this, 'ParserDeadLetterQueueUrl', { value: parserDeadLetterQueue.queueUrl });
  }

  /** Both tables are keyed by flight id (partition) and ISO-8601 UTC timestamp (sort). */
  private createFlightTable(id: string): dynamodb.Table {
    return new dynamodb.Table(this, id, {
      partitionKey: { name: 'flightId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
