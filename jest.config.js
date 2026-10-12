/** CDK infrastructure unit tests (synthesized template assertions, no AWS access needed). */
module.exports = {
  testEnvironment: 'node',
  roots: ['<rootDir>/aws/test'],
  testMatch: ['**/*.test.ts'],
  transform: {
    '^.+\\.tsx?$': ['@swc/jest'],
  },
  setupFilesAfterEnv: ['aws-cdk-lib/testhelpers/jest-autoclean'],
};
