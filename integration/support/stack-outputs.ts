import * as fs from 'node:fs';
import * as path from 'node:path';

const STACK_NAME = 'NextStepPosReports';
const OUTPUTS_FILE = path.join(__dirname, '..', '..', 'cdk.outputs', 'outputs.json');

export interface StackOutputs {
  region: string;
  apiUrl: string;
  bucketName: string;
  parsedReportsTableName: string;
  calculationResultsTableName: string;
  calculationDeadLetterQueueUrl: string;
}

/** Reads the outputs written by `npm run deploy` (cdk deploy --outputs-file cdk.outputs/outputs.json). */
export function loadStackOutputs(): StackOutputs {
  if (!fs.existsSync(OUTPUTS_FILE)) {
    throw new Error(`${OUTPUTS_FILE} not found. Deploy the stack first with "npm run deploy".`);
  }

  const outputs = JSON.parse(fs.readFileSync(OUTPUTS_FILE, 'utf8'))[STACK_NAME] as Record<string, string> | undefined;
  if (!outputs) {
    throw new Error(`No outputs for stack ${STACK_NAME} in ${OUTPUTS_FILE}.`);
  }

  return {
    region: outputs.Region,
    apiUrl: outputs.ApiUrl,
    bucketName: outputs.BucketName,
    parsedReportsTableName: outputs.ParsedReportsTableName,
    calculationResultsTableName: outputs.CalculationResultsTableName,
    calculationDeadLetterQueueUrl: outputs.CalculationDeadLetterQueueUrl,
  };
}
