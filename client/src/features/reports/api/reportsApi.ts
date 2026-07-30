import agent from '../../../app/api/agent';
import { ReportDefinition, DataSourceKind, FieldMeta, ParameterType, TextDirection } from '../types/reportDefinition';

export interface ReportTemplateSummary {
    id: number;
    code: string;
    name: string;
    description?: string;
    version: number;
    isActive: boolean;
    updatedAt: string;
    updatedBy?: string;
}

export interface PagedResult<T> {
    items: T[];
    total: number;
    skip: number;
    take: number;
}

export interface ValidationMessage {
    path: string;
    message: string;
}

export interface ReportTemplate {
    id: number;
    code: string;
    name: string;
    description?: string;
    version: number;
    isActive: boolean;
    createdAt: string;
    updatedAt: string;
    updatedBy?: string;
    definition: ReportDefinition;
    warnings: ValidationMessage[];
}

export interface CreatedTemplate {
    id: number;
    code: string;
    name: string;
    version: number;
}

export interface TemplateVersion {
    version: number;
    createdAt: string;
    createdBy?: string;
}

export interface LookupOption {
    value: string;
    label: string;
}

export interface ReportParameterInfo {
    name: string;
    label: string;
    type: ParameterType;
    required: boolean;
    defaultValue?: string;
    hidden: boolean;
    order: number;
    lookupTableId?: number;
    options?: LookupOption[];
}

export interface ReportParametersInfo {
    id: number;
    code: string;
    name: string;
    parameters: ReportParameterInfo[];
}

export interface TableSourceInputInfo {
    name: string;
    label?: string;
    dataType: string;
    required: boolean;
}

export interface TableSource {
    tableId: number;
    name: string;
    description?: string;
    sourceKind: DataSourceKind;
    fields: FieldMeta[];
    inputs: TableSourceInputInfo[];
}

export interface DataPreview {
    fields: FieldMeta[];
    rows: Record<string, unknown>[];
    truncated: boolean;
}

export interface GenerateOptions {
    culture?: string;
    direction?: TextDirection;
    inline?: boolean;
    fileName?: string;
}

/** A generated PDF plus the metadata the API returns alongside it in response headers. */
export interface GeneratedPdf {
    blob: Blob;
    pageCount: number;
    durationMs: number;
    truncated: boolean;
}

const templates = {
    list: (includeInactive = false) =>
        agent.get<PagedResult<ReportTemplateSummary>>('/report-templates', {
            params: { includeInactive, take: 200 }
        }).then(r => r.data),

    get: (id: number) =>
        agent.get<ReportTemplate>(`/report-templates/${id}`).then(r => r.data),

    create: (body: { code?: string; name?: string; description?: string; definition?: ReportDefinition }) =>
        agent.post<CreatedTemplate>('/report-templates', body).then(r => r.data),

    /**
     * Saves a design. expectedVersion turns this into an optimistic concurrency check, so a second
     * editor gets a 409 instead of silently overwriting the first.
     */
    update: (id: number, body: {
        name?: string;
        description?: string;
        definition: ReportDefinition;
        expectedVersion?: number;
    }) => agent.put<ReportTemplate>(`/report-templates/${id}`, body).then(r => r.data),

    remove: (id: number) => agent.delete(`/report-templates/${id}`).then(() => undefined),

    duplicate: (id: number, body: { code?: string; name?: string }) =>
        agent.post<CreatedTemplate>(`/report-templates/${id}/duplicate`, body).then(r => r.data),

    versions: (id: number) =>
        agent.get<TemplateVersion[]>(`/report-templates/${id}/versions`).then(r => r.data),

    restore: (id: number, version: number) =>
        agent.post<ReportTemplate>(`/report-templates/${id}/restore/${version}`).then(r => r.data),

    validate: (definition: ReportDefinition) =>
        agent.post<{ message: string; errors: ValidationMessage[] }>('/report-templates/validate', definition)
            .then(r => r.data)
};

/** Reads the metadata headers the API attaches to a generated PDF. */
function toGeneratedPdf(response: { data: Blob; headers: Record<string, unknown> }): GeneratedPdf {
    const header = (name: string) => Number(response.headers[name] ?? 0);

    return {
        blob: response.data,
        pageCount: header('x-report-page-count'),
        durationMs: header('x-report-duration-ms'),
        truncated: response.headers['x-report-truncated'] === 'true'
    };
}

const reports = {
    parameters: (idOrCode: string | number) =>
        agent.get<ReportParametersInfo>(`/reports/${idOrCode}/parameters`).then(r => r.data),

    generate: (idOrCode: string | number, body: {
        parameters?: Record<string, unknown>;
        data?: Record<string, unknown[]>;
        options?: GenerateOptions;
    }) => agent.post(`/reports/${idOrCode}/generate`, body, { responseType: 'blob' })
        .then(r => toGeneratedPdf(r as never)),

    /**
     * Renders an unsaved definition. This is what the designer's preview uses, which is why the preview
     * and the final PDF can never disagree — they are the same server render.
     */
    preview: (body: {
        definition: ReportDefinition;
        parameters?: Record<string, unknown>;
        data?: Record<string, unknown[]>;
        options?: GenerateOptions;
    }) => agent.post('/reports/preview', body, { responseType: 'blob' })
        .then(r => toGeneratedPdf(r as never))
};

const sources = {
    list: () => agent.get<TableSource[]>('/report-sources').then(r => r.data),

    fields: (tableId: number) =>
        agent.get<TableSource>(`/report-sources/${tableId}/fields`).then(r => r.data),

    sample: (tableId: number, inputs: Record<string, unknown> = {}) =>
        agent.post<DataPreview>(`/report-sources/${tableId}/sample`, inputs).then(r => r.data)
};

const reportsApi = { templates, reports, sources };
export default reportsApi;
