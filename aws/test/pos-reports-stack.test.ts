import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as cdk from 'aws-cdk-lib/core';
import { Match, Template } from 'aws-cdk-lib/assertions';
import { PosReportsStack } from '../lib/pos-reports-stack';

/** Same feature flags as `cdk synth` / `cdk deploy`, so the tests see the template that actually gets deployed. */
const CDK_CONTEXT = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'cdk.json'), 'utf8')).context;

const LAMBDA_PROJECTS = [
  'NextStep.PosReports.Lambda.ReceivePosReport',
  'NextStep.PosReports.Lambda.ParsePosReport',
  'NextStep.PosReports.Lambda.CalculateFlightStatus',
  'NextStep.PosReports.Lambda.GetFlightStatus',
];

type PolicyStatement = { Action: string | string[]; Resource: unknown };
type PolicyResource = { Properties: { PolicyDocument: { Statement: PolicyStatement[] }; Roles: { Ref: string }[] } };

/** Placeholder Lambda packages so the stack can be synthesized without running `dotnet publish`. */
function createFakeArtifacts(): string {
  const artifactsPath = fs.mkdtempSync(path.join(os.tmpdir(), 'pos-reports-artifacts-'));
  for (const project of LAMBDA_PROJECTS) {
    fs.mkdirSync(path.join(artifactsPath, project));
    fs.writeFileSync(path.join(artifactsPath, project, `${project}.dll`), '');
  }
  return artifactsPath;
}

function asArray<T>(value: T | T[]): T[] {
  return Array.isArray(value) ? value : [value];
}

const withoutLogicalIdHash = (logicalId: string) => logicalId.replace(/[0-9A-F]{8}$/, '');

/** Readable form of a policy resource: { Fn::GetAtt: [Bucket…, Arn] } + '/pos/*' → 'PosReportsBucket/pos/*'. */
function describeResource(resource: unknown): string {
  const value = resource as { 'Fn::GetAtt'?: [string, string]; 'Fn::Join'?: [string, unknown[]] };
  if (value['Fn::GetAtt']) {
    return withoutLogicalIdHash(value['Fn::GetAtt'][0]);
  }
  if (value['Fn::Join']) {
    return value['Fn::Join'][1].map((part) => (typeof part === 'string' ? part : describeResource(part))).join('');
  }
  return JSON.stringify(resource);
}

describe('PosReportsStack', () => {
  let template: Template;

  beforeAll(() => {
    const app = new cdk.App({ context: CDK_CONTEXT });
    const stack = new PosReportsStack(app, 'TestStack', { lambdaArtifactsPath: createFakeArtifacts() });
    template = Template.fromStack(stack);
  });

  describe('storage', () => {
    test('bucket is private, encrypted and rejects non-TLS requests', () => {
      template.hasResourceProperties('AWS::S3::Bucket', {
        PublicAccessBlockConfiguration: {
          BlockPublicAcls: true,
          BlockPublicPolicy: true,
          IgnorePublicAcls: true,
          RestrictPublicBuckets: true,
        },
        BucketEncryption: {
          ServerSideEncryptionConfiguration: [{ ServerSideEncryptionByDefault: { SSEAlgorithm: 'AES256' } }],
        },
      });
      template.hasResourceProperties('AWS::S3::BucketPolicy', {
        PolicyDocument: {
          Statement: Match.arrayWith([
            Match.objectLike({ Effect: 'Deny', Condition: { Bool: { 'aws:SecureTransport': 'false' } } }),
          ]),
        },
      });
    });

    test('two separate tables keyed by flightId and timestamp', () => {
      template.resourceCountIs('AWS::DynamoDB::Table', 2);
      template.allResourcesProperties('AWS::DynamoDB::Table', {
        KeySchema: [
          { AttributeName: 'flightId', KeyType: 'HASH' },
          { AttributeName: 'timestamp', KeyType: 'RANGE' },
        ],
        BillingMode: 'PAY_PER_REQUEST',
      });
    });
  });

  describe('triggers', () => {
    test('parser is notified only for objects created under pos/', () => {
      template.hasResourceProperties('Custom::S3BucketNotifications', {
        NotificationConfiguration: {
          LambdaFunctionConfigurations: [
            {
              Events: ['s3:ObjectCreated:*'],
              Filter: { Key: { FilterRules: [{ Name: 'prefix', Value: 'pos/' }] } },
            },
          ],
        },
      });
    });

    test('calculation queue has a dead-letter queue and a visibility timeout of 6x the calculator timeout', () => {
      template.hasResourceProperties('AWS::SQS::Queue', {
        VisibilityTimeout: 90,
        RedrivePolicy: {
          deadLetterTargetArn: Match.anyValue(),
          maxReceiveCount: 3,
        },
      });
    });

    test('calculator consumes the queue with partial batch failure reporting', () => {
      template.hasResourceProperties('AWS::Lambda::EventSourceMapping', {
        FunctionResponseTypes: ['ReportBatchItemFailures'],
      });
    });

    test('failed parser invocations go to a dead-letter queue after retries', () => {
      template.hasResourceProperties('AWS::Lambda::EventInvokeConfig', {
        MaximumRetryAttempts: 2,
        DestinationConfig: { OnFailure: { Destination: Match.anyValue() } },
      });
    });
  });

  describe('functions', () => {
    test('four .NET 8 arm64 functions with JSON logging', () => {
      expect(Object.keys(template.findResources('AWS::Lambda::Function', { Properties: { Runtime: 'dotnet8' } }))).toHaveLength(4);
      for (const project of LAMBDA_PROJECTS) {
        template.hasResourceProperties('AWS::Lambda::Function', {
          Handler: `${project}::${project}.Function::HandleAsync`,
          Runtime: 'dotnet8',
          Architectures: ['arm64'],
          LoggingConfig: Match.objectLike({ LogFormat: 'JSON', ApplicationLogLevel: 'INFO' }),
        });
      }
    });

    test('API exposes exactly POST /pos-reports and GET /status/{flightId}', () => {
      template.resourceCountIs('AWS::ApiGatewayV2::Route', 2);
      template.hasResourceProperties('AWS::ApiGatewayV2::Route', { RouteKey: 'POST /pos-reports' });
      template.hasResourceProperties('AWS::ApiGatewayV2::Route', { RouteKey: 'GET /status/{flightId}' });
    });
  });

  describe('least privilege IAM', () => {
    /**
     * Flattens a function role's policy into sorted "action resource" pairs, so the assertion does not depend on
     * how CDK merges or orders statements. Logical-id hashes are dropped: PosReportsBucket21419002 → PosReportsBucket.
     */
    const grantsOf = (rolePrefix: string): string[] => {
      const policies = Object.values(template.findResources('AWS::IAM::Policy') as Record<string, PolicyResource>)
        .filter((policy) => policy.Properties.Roles.some((role) => role.Ref.startsWith(rolePrefix)));
      expect(policies).toHaveLength(1);

      return policies[0].Properties.PolicyDocument.Statement
        .flatMap((statement) =>
          asArray(statement.Action).flatMap((action) =>
            asArray(statement.Resource).map((resource) => `${action} ${describeResource(resource)}`),
          ),
        )
        .sort();
    };

    test.each([
      [
        'ReceivePosReportRole',
        [
          'logs:CreateLogStream ReceivePosReportLogGroup',
          'logs:PutLogEvents ReceivePosReportLogGroup',
          's3:PutObject PosReportsBucket/pos/*',
        ],
      ],
      [
        'ParsePosReportRole',
        [
          'dynamodb:PutItem ParsedReportsTable',
          'logs:CreateLogStream ParsePosReportLogGroup',
          'logs:PutLogEvents ParsePosReportLogGroup',
          's3:GetObject PosReportsBucket/pos/*',
          's3:PutObject PosReportsBucket/attachment/*',
          'sqs:SendMessage CalculationQueue',
          'sqs:SendMessage ParserDeadLetterQueue',
        ],
      ],
      [
        'CalculateFlightStatusRole',
        [
          'dynamodb:GetItem ParsedReportsTable',
          'dynamodb:PutItem CalculationResultsTable',
          'logs:CreateLogStream CalculateFlightStatusLogGroup',
          'logs:PutLogEvents CalculateFlightStatusLogGroup',
          's3:GetObject PosReportsBucket/attachment/*',
          's3:PutObject PosReportsBucket/results/*',
          'sqs:DeleteMessage CalculationQueue',
          'sqs:GetQueueAttributes CalculationQueue',
          'sqs:ReceiveMessage CalculationQueue',
        ],
      ],
      [
        'GetFlightStatusRole',
        [
          'dynamodb:Query CalculationResultsTable',
          'dynamodb:Query ParsedReportsTable',
          'logs:CreateLogStream GetFlightStatusLogGroup',
          'logs:PutLogEvents GetFlightStatusLogGroup',
        ],
      ],
    ])('%s is granted exactly what the function uses', (rolePrefix, expectedGrants) => {
      expect(grantsOf(rolePrefix)).toEqual(expectedGrants);
    });

    test('no statement in the template grants a wildcard action or a wildcard resource', () => {
      const statements = Object.values(template.findResources('AWS::IAM::Policy') as Record<string, PolicyResource>)
        .flatMap((policy) => policy.Properties.PolicyDocument.Statement);
      expect(statements.length).toBeGreaterThan(0);

      for (const statement of statements) {
        for (const action of asArray(statement.Action)) {
          expect(action).not.toContain('*');
        }
        for (const resource of asArray(statement.Resource)) {
          expect(resource).not.toEqual('*');
        }
      }
    });

    test('function roles have no AWS managed policies attached', () => {
      const lambdaRoles = template.findResources('AWS::IAM::Role', {
        Properties: {
          AssumeRolePolicyDocument: {
            Statement: [Match.objectLike({ Principal: { Service: 'lambda.amazonaws.com' } })],
          },
          Description: Match.stringLikeRegexp('^Execution role for NextStep'),
        },
      });

      expect(Object.keys(lambdaRoles)).toHaveLength(4);
      for (const role of Object.values(lambdaRoles)) {
        expect(role.Properties.ManagedPolicyArns).toBeUndefined();
      }
    });
  });
});
