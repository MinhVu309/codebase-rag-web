import { api } from './api';
import type { ProjectSummaryDto, ProjectDetailDto, CreateProjectRequest, UpdateProjectRequest } from '@/types/api';

export const projectService = {
  async list(): Promise<ProjectSummaryDto[]> {
    const res = await api.get<ProjectSummaryDto[]>('/projects');
    return res.data;
  },

  async get(id: string): Promise<ProjectDetailDto> {
    const res = await api.get<ProjectDetailDto>(`/projects/${id}`);
    return res.data;
  },

  async create(data: CreateProjectRequest): Promise<ProjectDetailDto> {
    const res = await api.post<ProjectDetailDto>('/projects', data);
    return res.data;
  },

  async update(id: string, data: UpdateProjectRequest): Promise<ProjectDetailDto> {
    const res = await api.put<ProjectDetailDto>(`/projects/${id}`, data);
    return res.data;
  },

  async delete(id: string): Promise<void> {
    await api.delete(`/projects/${id}`);
  },
};
