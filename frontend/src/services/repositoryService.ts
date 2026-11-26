import { api } from './api';
import type { RepositoryDto, ImportRepositoryRequest, CodeFileDto } from '@/types/api';

export const repositoryService = {
  async import(projectId: string, data: ImportRepositoryRequest): Promise<RepositoryDto> {
    const res = await api.post<RepositoryDto>(`/projects/${projectId}/repositories`, data);
    return res.data;
  },

  async getStatus(repositoryId: string): Promise<RepositoryDto> {
    const res = await api.get<RepositoryDto>(`/repositories/${repositoryId}`);
    return res.data;
  },

  async delete(repositoryId: string): Promise<void> {
    await api.delete(`/repositories/${repositoryId}`);
  },

  async readFile(repositoryId: string, path: string, start?: number, end?: number): Promise<CodeFileDto> {
    const params = new URLSearchParams({ path });
    if (start !== undefined) params.append('start', start.toString());
    if (end !== undefined) params.append('end', end.toString());

    const res = await api.get<CodeFileDto>(`/repositories/${repositoryId}/files?${params.toString()}`);
    return res.data;
  },
};
