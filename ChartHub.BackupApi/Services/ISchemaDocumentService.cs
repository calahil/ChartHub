namespace ChartHub.BackupApi.Services;

public interface ISchemaDocumentService
{
    string BuildJsonSchema();

    string BuildOpenApiComponentsSchema();
}
