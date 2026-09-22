import { applyFilters, type FilterToken } from './filters'
import type { CatalogNode } from '../types'

self.onmessage = (event: MessageEvent<{ nodes: CatalogNode[]; tokens: FilterToken[] }>) => {
  self.postMessage(applyFilters(event.data.nodes, event.data.tokens).map((node) => node.id))
}
