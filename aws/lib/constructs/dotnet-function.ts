import * as path from 'node:path';
import * as cdk from 'aws-cdk-lib/core';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as logs from 'aws-cdk-lib/aws-logs';
import { Construct } from 'constructs';

export interface DotnetFunctionProps {
  /**
   * .NET project name under src/Lambdas. It is also the assembly name and root namespace,
   * and the folder name inside the artifacts directory produced by scripts/build-lambdas.sh.
   */
  readonly projectName: string;
  readonly description: string;
  /** Directory containing one published folder per Lambda project. */
  readonly artifactsPath: string;
  readonly timeout: cdk.Duration;
  readonly environment?: Record<string, string>;
}

/**
 * A .NET 8 Lambda function (arm64) with its own log group and a role that may only write to that log group.
 * Every other permission is added explicitly by the stack.
 */
export class DotnetFunction extends Construct {
  public readonly function: lambda.Function;

  constructor(scope: Construct, id: string, props: DotnetFunctionProps) {
    super(scope, id);

    const logGroup = new logs.LogGroup(this, 'LogGroup', {
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Not using the default AWSLambdaBasicExecutionRole: it allows writing to any log group (Resource "*").
    const role = new iam.Role(this, 'Role', {
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
      description: `Execution role for ${props.projectName}`,
    });
    logGroup.grantWrite(role);

    this.function = new lambda.Function(this, 'Function', {
      description: props.description,
      runtime: lambda.Runtime.DOTNET_8,
      architecture: lambda.Architecture.ARM_64,
      handler: `${props.projectName}::${props.projectName}.Function::HandleAsync`,
      code: lambda.Code.fromAsset(path.join(props.artifactsPath, props.projectName)),
      memorySize: 512,
      timeout: props.timeout,
      environment: props.environment,
      role,
      logGroup,
      loggingFormat: lambda.LoggingFormat.JSON,
      applicationLogLevelV2: lambda.ApplicationLogLevel.INFO,
      systemLogLevelV2: lambda.SystemLogLevel.WARN,
    });
  }

  /** Grants exactly the given actions on exactly the given resources. */
  public allow(actions: string[], resources: string[]): void {
    this.function.addToRolePolicy(new iam.PolicyStatement({ actions, resources }));
  }
}
