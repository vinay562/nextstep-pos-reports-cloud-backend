import { DynamoDBClient } from '@aws-sdk/client-dynamodb';
import { DynamoDBDocumentClient, QueryCommand } from '@aws-sdk/lib-dynamodb';
import { GetObjectCommand, ListObjectsV2Command, S3Client } from '@aws-sdk/client-s3';
import { DeleteMessageCommand, ReceiveMessageCommand, SQSClient } from '@aws-sdk/client-sqs';
import { StackOutputs } from './stack-outputs';

/** Read access to the deployed resources, used to verify what the pipeline stored. */
export class PipelineResources {
  private readonly s3: S3Client;
  private readonly dynamoDb: DynamoDBDocumentClient;
  private readonly sqs: SQSClient;

  constructor(private readonly outputs: StackOutputs) {
    this.s3 = new S3Client({ region: outputs.region });
    this.dynamoDb = DynamoDBDocumentClient.from(new DynamoDBClient({ region: outputs.region }));
    this.sqs = new SQSClient({ region: outputs.region });
  }

  public async listObjectKeys(prefix: string): Promise<string[]> {
    const response = await this.s3.send(new ListObjectsV2Command({ Bucket: this.outputs.bucketName, Prefix: prefix }));
    return (response.Contents ?? []).map((object) => object.Key!).sort();
  }

  public async readObject(key: string): Promise<string> {
    const response = await this.s3.send(new GetObjectCommand({ Bucket: this.outputs.bucketName, Key: key }));
    return response.Body!.transformToString();
  }

  public queryParsedReports(flightId: string): Promise<Record<string, unknown>[]> {
    return this.queryByFlightId(this.outputs.parsedReportsTableName, flightId);
  }

  public queryCalculationResults(flightId: string): Promise<Record<string, unknown>[]> {
    return this.queryByFlightId(this.outputs.calculationResultsTableName, flightId);
  }

  /** Finds (and removes) a dead-lettered calculation request for the given flight, if one has arrived. */
  public async takeDeadLetteredCalculationRequest(flightId: string): Promise<Record<string, unknown> | undefined> {
    const response = await this.sqs.send(
      new ReceiveMessageCommand({
        QueueUrl: this.outputs.calculationDeadLetterQueueUrl,
        MaxNumberOfMessages: 10,
        WaitTimeSeconds: 5,
        VisibilityTimeout: 5,
      }),
    );

    const message = (response.Messages ?? []).find((candidate) => candidate.Body?.includes(flightId));
    if (!message) {
      return undefined;
    }

    await this.sqs.send(
      new DeleteMessageCommand({ QueueUrl: this.outputs.calculationDeadLetterQueueUrl, ReceiptHandle: message.ReceiptHandle }),
    );
    return JSON.parse(message.Body!) as Record<string, unknown>;
  }

  private async queryByFlightId(tableName: string, flightId: string): Promise<Record<string, unknown>[]> {
    const response = await this.dynamoDb.send(
      new QueryCommand({
        TableName: tableName,
        KeyConditionExpression: 'flightId = :flightId',
        ExpressionAttributeValues: { ':flightId': flightId },
        ConsistentRead: true,
      }),
    );
    return response.Items ?? [];
  }
}
