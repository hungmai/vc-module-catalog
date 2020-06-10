using System;
using VirtoCommerce.CatalogModule.Core.Model.DynamicAssociations;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.CatalogModule.Core.Model
{
    public class DynamicAssociation : AuditableEntity, IHasOuterId
    {
        public string AssociationType { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public bool IsActive { get; set; }

        public string StoreId { get; set; }

        public int Priority { get; set; }

        public string OuterId { get; set; }

        public DynamicAssociationRuleTree ExpressionTree { get; set; }
    }
}
