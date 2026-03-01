using Amazon;
using Amazon.CDK;
using Amazon.CDK.AWS.DSQL;

#pragma warning disable ASPIREAWSPUBLISHERS001

var builder = DistributedApplication.CreateBuilder(args);

var awsRegion = builder.Configuration["AWS:Region"] ?? "us-east-1";
var awsProfile = builder.Configuration["AWS:Profile"];

var awsConfig = builder.AddAWSSDKConfig()
    .WithRegion(RegionEndpoint.GetBySystemName(awsRegion));

if (!string.IsNullOrWhiteSpace(awsProfile))
{
    awsConfig.WithProfile(awsProfile);
}

builder.AddAWSCDKEnvironment(
    name: "GameBackend",
    cdkDefaultsProviderFactory: Aspire.Hosting.AWS.Deployment.CDKDefaultsProviderFactory.Preview_V1);

var infraStack = builder.AddAWSCDKStack("gamebackend-infra")
    .WithReference(awsConfig);

var dsqlCluster = infraStack.AddConstruct(
    "gamebackend-dsql-cluster",
    scope => new CfnCluster(scope, "GameBackendDsqlCluster", new CfnClusterProps
    {
        DeletionProtectionEnabled = false,
        Tags =
        [
            new CfnTag
            {
                Key = "Name",
                Value = "gamebackend-local-dev-dsql-cluster"
            }
        ]
    }));

var migrations = builder.AddProject<Projects.GameBackend_Migrations>("gamebackend-migrations")
    .WaitFor(infraStack)
    .WithReference(awsConfig)
    .WithReference(dsqlCluster, cluster => cluster.AttrEndpoint, "GameBackendDsqlClusterEndpoint")
    .WithEnvironment("AWS_REGION", awsRegion);

if (!string.IsNullOrWhiteSpace(awsConfig.Profile))
{
    migrations.WithEnvironment("AWS_PROFILE", awsConfig.Profile);
}

var backend = builder.AddProject<Projects.GameBackend>("backend")
    .WaitFor(infraStack)
    .WaitForCompletion(migrations)
    .WithReference(awsConfig)
    .WithReference(dsqlCluster, cluster => cluster.AttrEndpoint, "GameBackendDsqlClusterEndpoint")
    .WithEnvironment("AWS_REGION", awsRegion);

if (!string.IsNullOrWhiteSpace(awsConfig.Profile))
{
    backend.WithEnvironment("AWS_PROFILE", awsConfig.Profile);
}

builder.Build().Run();
