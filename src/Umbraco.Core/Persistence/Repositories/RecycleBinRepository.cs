﻿using System;
using System.Collections.Generic;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    internal class RecycleBinRepository
    {
        private readonly IDatabaseUnitOfWork _unitOfWork;

        public RecycleBinRepository(IDatabaseUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public bool EmptyRecycleBin(Guid nodeObjectType)
        {
            try
            {
                var db = _unitOfWork.Database;

                //Issue query to get all trashed content or media that has the Upload field as a property
                //The value for each field is stored in a list: FilesToDelete<string>()
                //Alias: Constants.Conventions.Media.File and ControlId: Constants.PropertyEditors.UploadField
                var sql = new Sql();
                sql.Select("DISTINCT(dataNvarchar)")
                    .From<PropertyDataDto>()
                    .InnerJoin<NodeDto>().On<PropertyDataDto, NodeDto>(left => left.NodeId, right => right.NodeId)
                    .InnerJoin<PropertyTypeDto>().On<PropertyDataDto, PropertyTypeDto>(left => left.PropertyTypeId, right => right.Id)
                    .InnerJoin<DataTypeDto>().On<PropertyTypeDto, DataTypeDto>(left => left.DataTypeId, right => right.DataTypeId)
                    .Where("umbracoNode.trashed = '1' AND umbracoNode.nodeObjectType = @NodeObjectType AND dataNvarchar IS NOT NULL AND (cmsPropertyType.Alias = @FileAlias OR cmsDataType.controlId = @ControlId)",
                        new { FileAlias = Constants.Conventions.Media.File, NodeObjectType = nodeObjectType, ControlId = Constants.PropertyEditors.UploadField });

                var files = db.Fetch<string>(sql);

                //Construct and execute delete statements for all trashed items by 'nodeObjectType'
                var deletes = new List<string>
                          {
                              FormatDeleteStatement("umbracoUser2NodeNotify", "nodeId"),
                              FormatDeleteStatement("umbracoUser2NodePermission", "nodeId"),
                              FormatDeleteStatement("umbracoRelation", "parentId"),
                              FormatDeleteStatement("umbracoRelation", "childId"),
                              FormatDeleteStatement("cmsTagRelationship", "nodeId"),
                              FormatDeleteStatement("umbracoDomains", "domainRootStructureID"),
                              FormatDeleteStatement("cmsDocument", "NodeId"),
                              FormatDeleteStatement("cmsPropertyData", "contentNodeId"),
                              FormatDeleteStatement("cmsPreviewXml", "nodeId"),
                              FormatDeleteStatement("cmsContentVersion", "ContentId"),
                              FormatDeleteStatement("cmsContentXml", "nodeID"),
                              FormatDeleteStatement("cmsContent", "NodeId"),
                              "DELETE FROM umbracoNode WHERE trashed = '1' AND nodeObjectType = @NodeObjectType"
                          };

                foreach (var delete in deletes)
                {
                    db.Execute(delete, new { NodeObjectType = nodeObjectType });
                }

                //Trigger (internal) event with list of files to delete - RecycleBinEmptied

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error<RecycleBinRepository>("An error occurred while emptying the Recycle Bin: " + ex.Message, ex);
                return false;
            }
        }

        private string FormatDeleteStatement(string tableName, string keyName)
        {
            return
                string.Format(
                    "DELETE FROM {0} FROM {0} as TB1 INNER JOIN umbracoNode as TB2 ON TB1.{1} = TB2.id WHERE TB2.trashed = '1' AND TB2.nodeObjectType = @NodeObjectType",
                    tableName, keyName);
        }
    }
}