#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib/core';
import { PosReportsStack } from '../lib/pos-reports-stack';

const app = new cdk.App();

new PosReportsStack(app, 'NextStepPosReports', {
  description: 'POS report processing pipeline: receive, parse and calculate flight status.',
  // Account and region come from the active AWS CLI profile, so the stack is not tied to one environment.
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION,
  },
});
