import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as cdk from 'aws-cdk-lib/core';
import { Match, Template } from 'aws-cdk-lib/assertions';
import { PosReportsStack } from '../lib/pos-reports-stack';

const LAMBDA_PROJECTS = [
  'NextStep.PosReports.Lambda.ReceivePosReport',
  'NextStep.PosReports.Lambda.ParsePosReport',
  'NextStep.PosReports.Lambda.CalculateFlightStatus',
  'NextStep.PosReports.Lambda.GetFlightStatus',
];

type PolicyStatement = { Action: string | string[]; Resource: unknown };
type PolicyResource = { Properties: { PolicyDocument: { Statement: PolicyStatement[] } } };

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

describe('PosReportsStack', () => {
  let template: Template;

  beforeAll(() => {
    const app = new cdk.App();
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
    test('no statement in the template grants a wildcard action or a wildcard resource', () => {
      const policies = Object.entries(template.findResources('AWS::IAM::Policy') as Record<string, PolicyResource>);
      expect(policies.length).toBeGreaterThanOrEqual(4);

      for (const [policyId, policy] of policies) {
        for (const statement of policy.Properties.PolicyDocument.Statement) {
          for (const action of asArray(statement.Action)) {
            expect({ policyId, action }).toEqual({ policyId, action: expect.not.stringContaining('*') });
          }
          for (const resource of asArray(statement.Resource)) {
            expect({ policyId, resource }).not.toEqual({ policyId, resource: '*' });
          }
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

    test('receive function may only write raw reports under pos/', () => {
      template.hasResourceProperties('AWS::IAM::Policy', {
        PolicyDocument: {
          Statement: Match.arrayWith([
            {
              Action: 's3:PutObject',
              Effect: 'Allow',
              Resource: { 'Fn::Join': ['', [{ 'Fn::GetAtt': [Match.stringLikeRegexp('PosReportsBucket'), 'Arn'] }, '/pos/*']] },
            },
          ]),
        },
      });
    });

    test('status function may only query the two tables', () => {
      template.hasResourceProperties('AWS::IAM::Policy', {
        PolicyDocument: {
          Statement: Match.arrayWith([
            {
              Action: 'dynamodb:Query',
              Effect: 'Allow',
              Resource: [
                { 'Fn::GetAtt': [Match.stringLikeRegexp('ParsedReportsTable'), 'Arn'] },
                { 'Fn::GetAtt': [Match.stringLikeRegexp('CalculationResultsTable'), 'Arn'] },
              ],
            },
          ]),
        },
      });
    });
  });
});
