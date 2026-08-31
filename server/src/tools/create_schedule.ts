import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateScheduleTool(server: McpServer) {
  server.tool(
    "create_schedule",
    "Create one or more schedules in Revit. Supports regular schedules, material takeoffs, key schedules, view lists, sheet lists, and revision schedules. Specify the element category by category ID or name.",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .optional()
              .describe("Schedule name"),
            type: z
              .string()
              .optional()
              .describe(
                "Schedule type: regular, material, keynote, viewList, sheetList, or revision"
              ),
            categoryId: z
              .number()
              .optional()
              .describe("Category ID for the schedule"),
            categoryName: z
              .string()
              .optional()
              .describe("Category name for the schedule"),
            templateId: z
              .string()
              .optional()
              .describe("Template view ID to apply"),
            showTitle: z
              .boolean()
              .optional()
              .describe("Show schedule title"),
            showHeaders: z
              .boolean()
              .optional()
              .describe("Show column headers"),
            showGridLines: z
              .boolean()
              .optional()
              .describe("Show grid lines"),
            showOutlines: z
              .boolean()
              .optional()
              .describe("Show outlines"),
            fields: z
              .array(
                z.object({
                  parameterId: z.number().optional().describe("Parameter ID"),
                  parameterName: z.string().optional().describe("Parameter name"),
                  fieldType: z
                    .string()
                    .optional()
                    .describe("Field type: Instance, Type, Count, or Formula"),
                  heading: z.string().optional().describe("Column heading"),
                  isCalculatedField: z
                    .boolean()
                    .optional()
                    .describe("Whether this is a calculated field"),
                  formula: z.string().optional().describe("Formula for calculated fields"),
                  width: z.number().optional().describe("Column width in pixels"),
                  isHidden: z.boolean().optional().describe("Whether the field is hidden"),
                  horizontalAlignment: z
                    .string()
                    .optional()
                    .describe("Horizontal alignment: Left, Center, or Right"),
                  formatOption: z.string().optional().describe("Format option"),
                  accuracy: z.number().optional().describe("Decimal precision"),
                  useThousandSeparator: z
                    .boolean()
                    .optional()
                    .describe("Use a thousand separator"),
                })
              )
              .optional()
              .describe("Fields to include in the schedule"),
            filters: z
              .array(
                z.object({
                  fieldName: z.string().optional().describe("Field name to filter by"),
                  fieldIndex: z.number().optional().describe("Field index"),
                  filterType: z
                    .string()
                    .optional()
                    .describe("Filter type: Equal, NotEqual, GreaterThan, etc."),
                  filterValue: z.string().optional().describe("Filter value"),
                })
              )
              .optional()
              .describe("Filters to apply"),
            clearExistingFilters: z
              .boolean()
              .optional()
              .describe("Clear existing filters before applying new ones"),
            sortFields: z
              .array(
                z.object({
                  fieldName: z.string().optional().describe("Field name to sort by"),
                  fieldIndex: z.number().optional().describe("Field index"),
                  sortOrder: z
                    .string()
                    .optional()
                    .describe("Sort order: Ascending or Descending"),
                })
              )
              .optional()
              .describe("Fields to sort by"),
            clearExistingSorts: z
              .boolean()
              .optional()
              .describe("Clear existing sort fields before applying new ones"),
            groupFields: z
              .array(
                z.object({
                  fieldName: z.string().optional().describe("Field name to group by"),
                  fieldIndex: z.number().optional().describe("Field index"),
                  sortOrder: z
                    .string()
                    .optional()
                    .describe("Sort order: Ascending or Descending"),
                  showHeader: z.boolean().optional().describe("Show the group header"),
                  showFooter: z.boolean().optional().describe("Show the group footer"),
                  showBlankLine: z.boolean().optional().describe("Show a blank line after each group"),
                })
              )
              .optional()
              .describe("Fields to group by"),
            clearExistingGroups: z
              .boolean()
              .optional()
              .describe("Clear existing group fields before applying new ones"),
            parameters: z
              .record(z.any())
              .optional()
              .describe("Additional schedule parameters"),
          })
        )
        .describe("Array of schedule definitions to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Create schedule failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
