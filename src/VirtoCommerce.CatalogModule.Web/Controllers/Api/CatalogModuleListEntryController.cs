using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.CatalogModule.Core;
using VirtoCommerce.CatalogModule.Core.Model;
using VirtoCommerce.CatalogModule.Core.Model.ListEntry;
using VirtoCommerce.CatalogModule.Core.Model.Search;
using VirtoCommerce.CatalogModule.Core.Search;
using VirtoCommerce.CatalogModule.Core.Services;
using VirtoCommerce.CatalogModule.Data.Authorization;
using VirtoCommerce.CatalogModule.Data.Services;
using VirtoCommerce.CatalogModule.Web.Model;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.CatalogModule.Web.Controllers.Api
{
    [Route("api/catalog/listentries")]
    [Authorize]
    public class CatalogModuleListEntryController : Controller
    {
        private readonly IProductIndexedSearchService _productIndexedSearchService;
        private readonly ICategoryIndexedSearchService _categoryIndexedSearchService;
        private readonly ICategoryService _categoryService;
        private readonly ICatalogService _catalogService;
        private readonly IItemService _itemService;
        private readonly IListEntrySearchService _listEntrySearchService;
        private readonly ISettingsManager _settingsManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly ListEntryMover<Category> _categoryMover;
        private readonly ListEntryMover<CatalogProduct> _productMover;

        public CatalogModuleListEntryController(
            IProductIndexedSearchService productIndexedSearchService,
            ICategoryIndexedSearchService categoryIndexedSearchService,
            IListEntrySearchService listEntrySearchService,
            ICategoryService categoryService,
            IItemService itemService,
            ICatalogService catalogService,
            IAuthorizationService authorizationService,
            ISettingsManager settingsManager,
            ListEntryMover<Category> categoryMover,
            ListEntryMover<CatalogProduct> productMover)
        {
            _productIndexedSearchService = productIndexedSearchService;
            _categoryIndexedSearchService = categoryIndexedSearchService;
            _categoryService = categoryService;
            _authorizationService = authorizationService;
            _itemService = itemService;
            _catalogService = catalogService;
            _listEntrySearchService = listEntrySearchService;
            _settingsManager = settingsManager;
            _categoryMover = categoryMover;
            _productMover = productMover;
        }

        /// <summary>
        /// Searches for the items by complex criteria.
        /// </summary>
        /// <param name="criteria">The search criteria.</param>
        /// <returns></returns>
        [HttpPost]
        [Route("")]
        public async Task<ActionResult<ListEntrySearchResult>> ListItemsSearchAsync([FromBody] CatalogListEntrySearchCriteria criteria)
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, criteria, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Read));
            if (!authorizationResult.Succeeded)
            {
                return Unauthorized();
            }

            var result = await InnerListItemsSearchAsync(criteria);

            return Ok(result);
        }


        /// <summary>
        /// Creates links for categories or items to parent categories and catalogs.
        /// </summary>
        /// <param name="links">The links.</param>
        [HttpPost]
        [Route("~/api/catalog/listentrylinks")]
        public async Task<ActionResult> CreateLinks([FromBody] CategoryLink[] links)
        {
            var entryIds = links.Select(x => x.EntryId).ToArray();
            var hasLinkEntries = await LoadCatalogEntriesAsync<IHasLinks>(entryIds);

            var authorizationResult = await _authorizationService.AuthorizeAsync(User, hasLinkEntries, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Update));
            if (!authorizationResult.Succeeded)
            {
                return Unauthorized();
            }

            foreach (var link in links)
            {
                var hasLinkEntry = hasLinkEntries.FirstOrDefault(x => x.Id.Equals(link.EntryId));
                if (hasLinkEntry != null && !hasLinkEntry.Links.Contains(link))
                {
                    hasLinkEntry.Links.Add(link);
                }
            }

            if (!hasLinkEntries.IsNullOrEmpty())
            {
                await SaveListCatalogEntitiesAsync(hasLinkEntries.ToArray());
            }

            return Ok();
        }

        /// <summary>
        /// Bulk create links to categories and items
        /// </summary>
        /// <param name="creationRequest"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("~/api/catalog/listentrylinks/bulkcreate")]
        public async Task<ActionResult> BulkCreateLinks([FromBody] BulkLinkCreationRequest creationRequest)
        {
            if (creationRequest.CatalogId.IsNullOrEmpty())
            {
                return BadRequest("Target catalog identifier should be specified.");
            }

            var searchCriteria = creationRequest.SearchCriteria;
            bool haveProducts;

            do
            {
                var searchResult = await _listEntrySearchService.SearchAsync(searchCriteria);
                var hasLinkEntries = await LoadCatalogEntriesAsync<IHasLinks>(searchResult.ListEntries.Select(x => x.Id).ToArray());
                haveProducts = hasLinkEntries.Any();

                searchCriteria.Skip += searchCriteria.Take;

                var authorizationResult = await _authorizationService.AuthorizeAsync(User, hasLinkEntries, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Update));
                if (!authorizationResult.Succeeded)
                {
                    return Unauthorized();
                }

                foreach (var hasLinkEntry in hasLinkEntries)
                {
                    var link = AbstractTypeFactory<CategoryLink>.TryCreateInstance();
                    link.CategoryId = creationRequest.CategoryId;
                    link.CatalogId = creationRequest.CatalogId;
                    hasLinkEntry.Links.Add(link);
                }

                if (haveProducts)
                {
                    await SaveListCatalogEntitiesAsync(hasLinkEntries.ToArray());
                }
            } while (haveProducts);

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet]
        [Route("~/api/catalog/getslug")]
        public ActionResult<string> GetSlug(string text)
        {
            return Ok(text.GenerateSlug());
        }

        /// <summary>
        /// Unlinks the linked categories or items from parent categories and catalogs.
        /// </summary>
        /// <param name="links">The links.</param>
        [HttpPost]
        [Route("~/api/catalog/listentrylinks/delete")]
        [ProducesResponseType(typeof(void), StatusCodes.Status204NoContent)]
        public async Task<ActionResult> DeleteLinks([FromBody] CategoryLink[] links)
        {
            var entryIds = links.Select(x => x.EntryId).ToArray();
            var hasLinkEntries = await LoadCatalogEntriesAsync<IHasLinks>(entryIds);

            var authorizationResult = await _authorizationService.AuthorizeAsync(User, hasLinkEntries, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Delete));
            if (!authorizationResult.Succeeded)
            {
                return Unauthorized();
            }

            foreach (var link in links)
            {
                var hasLinkEntry = hasLinkEntries.FirstOrDefault(x => x.Id.Equals(link.EntryId));
                hasLinkEntry?.Links.Remove(link);
            }

            if (!hasLinkEntries.IsNullOrEmpty())
            {
                await SaveListCatalogEntitiesAsync(hasLinkEntries.ToArray());
            }

            return NoContent();
        }

        /// <summary>
        /// Move categories or products to another location.
        /// </summary>
        /// <param name="moveRequest">Move operation request</param>
        [HttpPost]
        [Route("move")]
        [ProducesResponseType(typeof(void), StatusCodes.Status204NoContent)]
        public async Task<ActionResult> Move([FromBody] ListEntriesMoveRequest moveRequest)
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, moveRequest, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Update));
            if (!authorizationResult.Succeeded)
            {
                return Unauthorized();
            }

            var dstCatalog = (await _catalogService.GetByIdsAsync(new[] {moveRequest.Catalog})).FirstOrDefault();
            if (dstCatalog.IsVirtual)
            {
                return BadRequest("Unable to move to a virtual catalog");
            }

            var categories = await _categoryMover.PrepareMoveAsync(moveRequest);
            var products = await _productMover.PrepareMoveAsync(moveRequest);

            await _categoryMover.ConfirmMoveAsync(categories);
            await _productMover.ConfirmMoveAsync(products);

            return NoContent();
        }

        /// <summary>
        /// Bulk delete by the search criteria.
        /// </summary>
        /// <param name="criteria"></param>
        [HttpPost]
        [Route("delete")]
        [ProducesResponseType(typeof(void), StatusCodes.Status204NoContent)]
        public async Task<ActionResult> Delete([FromBody] CatalogListEntrySearchCriteria criteria)
        {
            const int deleteBatchSize = 20;
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, criteria, new CatalogAuthorizationRequirement(ModuleConstants.Security.Permissions.Delete));
            if (!authorizationResult.Succeeded)
            {
                return Unauthorized();
            }

            var idsToDelete = criteria.ObjectIds?.ToList() ?? new List<string>();

            if (idsToDelete.IsNullOrEmpty())
            {
                var listEntries = await InnerListItemsSearchAsync(criteria);
                idsToDelete = listEntries.ListEntries
                    .Where(x => x.Type.EqualsInvariant(ProductListEntry.TypeName) || x.Type.EqualsInvariant(CategoryListEntry.TypeName))
                    .Select(x => x.Id)
                    .ToList();
            }

            for (var i = 0; i < idsToDelete.Count; i += deleteBatchSize)
            {
                var commonIds = idsToDelete.Skip(i).Take(deleteBatchSize).ToArray();

                var searchProductResult = await _itemService.GetByIdsAsync(commonIds, ItemResponseGroup.None.ToString());
                await _itemService.DeleteAsync(searchProductResult.Select(x => x.Id).ToArray());

                var searchCategoryResult = await _categoryService.GetByIdsAsync(commonIds, CategoryResponseGroup.None.ToString());
                await _categoryService.DeleteAsync(searchCategoryResult.Select(x => x.Id).ToArray());
            }

            return StatusCode(StatusCodes.Status204NoContent);
        }

        private async Task SaveListCatalogEntitiesAsync(IEntity[] entities)
        {
            if (!entities.IsNullOrEmpty())
            {
                var products = entities.OfType<CatalogProduct>().ToArray();
                if (!products.IsNullOrEmpty())
                {
                    await _itemService.SaveChangesAsync(products);
                }

                var categories = entities.OfType<Category>().ToArray();
                if (!categories.IsNullOrEmpty())
                {
                    await _categoryService.SaveChangesAsync(categories);
                }
            }
        }

        private async Task<IList<T>> LoadCatalogEntriesAsync<T>(string[] ids)
        {
            var products = await _itemService.GetByIdsAsync(ids, (ItemResponseGroup.Links | ItemResponseGroup.Variations).ToString());
            var categories = await _categoryService.GetByIdsAsync(ids.Except(products.Select(x => x.Id)).ToArray(), CategoryResponseGroup.WithLinks.ToString());
            return products.OfType<T>().Concat(categories.OfType<T>()).ToList();
        }

        private async Task<ListEntrySearchResult> InnerListItemsSearchAsync(CatalogListEntrySearchCriteria criteria)
        {
            var result = new ListEntrySearchResult();
            var useIndexedSearch = _settingsManager.GetValue(ModuleConstants.Settings.Search.UseCatalogIndexedSearchInManager.Name, true);

            if (useIndexedSearch && !string.IsNullOrEmpty(criteria.Keyword))
            {
                // TODO: create outline for category
                // TODO: implement sorting

                var categoryIndexedSearchCriteria = AbstractTypeFactory<CategoryIndexedSearchCriteria>.TryCreateInstance().FromListEntryCriteria(criteria) as CategoryIndexedSearchCriteria;
                const CategoryResponseGroup catResponseGroup = CategoryResponseGroup.Info | CategoryResponseGroup.WithOutlines;
                categoryIndexedSearchCriteria.ResponseGroup = catResponseGroup.ToString();

                var catIndexedSearchResult = await _categoryIndexedSearchService.SearchAsync(categoryIndexedSearchCriteria);
                var totalCount = catIndexedSearchResult.TotalCount;
                var skip = Math.Min(totalCount, criteria.Skip);
                var take = Math.Min(criteria.Take, Math.Max(0, totalCount - criteria.Skip));

                result.Results = catIndexedSearchResult.Items.Select(x => AbstractTypeFactory<CategoryListEntry>.TryCreateInstance().FromModel(x)).ToList();

                criteria.Skip -= (int) skip;
                criteria.Take -= (int) take;

                const ItemResponseGroup itemResponseGroup = ItemResponseGroup.ItemInfo | ItemResponseGroup.Outlines;

                var productIndexedSearchCriteria = AbstractTypeFactory<ProductIndexedSearchCriteria>.TryCreateInstance().FromListEntryCriteria(criteria) as ProductIndexedSearchCriteria;
                productIndexedSearchCriteria.ResponseGroup = itemResponseGroup.ToString();

                var indexedSearchResult = await _productIndexedSearchService.SearchAsync(productIndexedSearchCriteria);
                result.TotalCount += (int) indexedSearchResult.TotalCount;
                result.Results.AddRange(indexedSearchResult.Items.Select(x => AbstractTypeFactory<ProductListEntry>.TryCreateInstance().FromModel(x)));
            }
            else
            {
                result = await _listEntrySearchService.SearchAsync(criteria);
            }

            return result;
        }
    }
}
