module.exports = {
  preset: "ts-jest",
  testEnvironment: "node",
  roots: ["<rootDir>/__tests__"],
  testMatch: ["**/*.test.ts"],
  transform: {
    "^.+\\.ts$": ["ts-jest", {
      tsconfig: {
        target: "ES2022",
        module: "commonjs",
        lib: ["ES2022"],
        strict: true,
        esModuleInterop: true,
        skipLibCheck: true,
        types: ["jest", "node"],
      },
    }],
  },
};
