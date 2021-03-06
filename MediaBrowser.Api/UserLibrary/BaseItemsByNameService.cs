﻿using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Api.UserLibrary
{
    /// <summary>
    /// Class BaseItemsByNameService
    /// </summary>
    /// <typeparam name="TItemType">The type of the T item type.</typeparam>
    public abstract class BaseItemsByNameService<TItemType> : BaseApiService
        where TItemType : BaseItem, IItemByName
    {
        /// <summary>
        /// The _user manager
        /// </summary>
        protected readonly IUserManager UserManager;
        /// <summary>
        /// The library manager
        /// </summary>
        protected readonly ILibraryManager LibraryManager;
        protected readonly IUserDataManager UserDataRepository;
        protected readonly IItemRepository ItemRepository;
        protected IDtoService DtoService { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseItemsByNameService{TItemType}" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="userDataRepository">The user data repository.</param>
        /// <param name="itemRepository">The item repository.</param>
        /// <param name="dtoService">The dto service.</param>
        protected BaseItemsByNameService(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataRepository, IItemRepository itemRepository, IDtoService dtoService)
        {
            UserManager = userManager;
            LibraryManager = libraryManager;
            UserDataRepository = userDataRepository;
            ItemRepository = itemRepository;
            DtoService = dtoService;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Task{ItemsResult}.</returns>
        protected ItemsResult GetResult(GetItemsByName request)
        {
            var dtoOptions = GetDtoOptions(request);

            User user = null;
            BaseItem parentItem;
            List<BaseItem> libraryItems = null;

            if (request.UserId.HasValue)
            {
                user = UserManager.GetUserById(request.UserId.Value);
                parentItem = string.IsNullOrEmpty(request.ParentId) ? user.RootFolder : LibraryManager.GetItemById(request.ParentId);

                if (RequiresLibraryItems(request, dtoOptions))
                {
                    libraryItems = user.RootFolder.GetRecursiveChildren(user).ToList();
                }
            }
            else
            {
                parentItem = string.IsNullOrEmpty(request.ParentId) ? LibraryManager.RootFolder : LibraryManager.GetItemById(request.ParentId);
                if (RequiresLibraryItems(request, dtoOptions))
                {
                    libraryItems = LibraryManager.RootFolder.GetRecursiveChildren().ToList();
                }
            }

            IEnumerable<BaseItem> items;

            var excludeItemTypes = request.GetExcludeItemTypes();
            var includeItemTypes = request.GetIncludeItemTypes();
            var mediaTypes = request.GetMediaTypes();

            Func<BaseItem, bool> filter = i => FilterItem(request, i, excludeItemTypes, includeItemTypes, mediaTypes);

            if (parentItem.IsFolder)
            {
                var folder = (Folder)parentItem;

                if (request.UserId.HasValue)
                {
                    items = request.Recursive ?
                        folder.GetRecursiveChildren(user, filter) :
                        folder.GetChildren(user, true).Where(filter);
                }
                else
                {
                    items = request.Recursive ?
                        folder.GetRecursiveChildren(filter) :
                        folder.Children.Where(filter);
                }
            }
            else
            {
                items = new[] { parentItem }.Where(filter);
            }

            var extractedItems = GetAllItems(request, items);

            var filteredItems = FilterItems(request, extractedItems, user);

            filteredItems = FilterByLibraryItems(request, filteredItems, user, libraryItems);

            filteredItems = LibraryManager.Sort(filteredItems, user, request.GetOrderBy(), request.SortOrder ?? SortOrder.Ascending).Cast<TItemType>();

            var ibnItemsArray = filteredItems.ToList();

            IEnumerable<TItemType> ibnItems = ibnItemsArray;

            var result = new ItemsResult
            {
                TotalRecordCount = ibnItemsArray.Count
            };

            if (request.StartIndex.HasValue || request.Limit.HasValue)
            {
                if (request.StartIndex.HasValue)
                {
                    ibnItems = ibnItems.Skip(request.StartIndex.Value);
                }

                if (request.Limit.HasValue)
                {
                    ibnItems = ibnItems.Take(request.Limit.Value);
                }

            }

            IEnumerable<Tuple<TItemType, List<BaseItem>>> tuples;
            if (dtoOptions.Fields.Contains(ItemFields.ItemCounts))
            {
                tuples = ibnItems.Select(i => new Tuple<TItemType, List<BaseItem>>(i, i.GetTaggedItems(libraryItems).ToList()));
            }
            else
            {
                tuples = ibnItems.Select(i => new Tuple<TItemType, List<BaseItem>>(i, new List<BaseItem>()));
            }

            var dtos = tuples.Select(i => DtoService.GetItemByNameDto(i.Item1, dtoOptions, i.Item2, user));

            result.Items = dtos.Where(i => i != null).ToArray();

            return result;
        }

        private bool RequiresLibraryItems(GetItemsByName request, DtoOptions options)
        {
            var filters = request.GetFilters().ToList();

            if (filters.Contains(ItemFilter.IsPlayed))
            {
                return true;
            }

            if (filters.Contains(ItemFilter.IsUnplayed))
            {
                return true;
            }

            if (request.IsPlayed.HasValue)
            {
                return true;
            }

            return options.Fields.Contains(ItemFields.ItemCounts);
        }

        private IEnumerable<TItemType> FilterByLibraryItems(GetItemsByName request, IEnumerable<TItemType> items, User user, IEnumerable<BaseItem> libraryItems)
        {
            var filters = request.GetFilters().ToList();

            if (filters.Contains(ItemFilter.IsPlayed))
            {
                items = items.Where(i => i.GetTaggedItems(libraryItems).All(l => l.IsPlayed(user)));
            }

            if (filters.Contains(ItemFilter.IsUnplayed))
            {
                items = items.Where(i => i.GetTaggedItems(libraryItems).All(l => l.IsUnplayed(user)));
            }

            if (request.IsPlayed.HasValue)
            {
                var val = request.IsPlayed.Value;

                items = items.Where(i => i.GetTaggedItems(libraryItems).All(l => l.IsPlayed(user)) == val);
            }

            return items;
        }

        /// <summary>
        /// Filters the items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{`0}.</returns>
        private IEnumerable<TItemType> FilterItems(GetItemsByName request, IEnumerable<TItemType> items, User user)
        {
            if (!string.IsNullOrEmpty(request.NameStartsWithOrGreater))
            {
                items = items.Where(i => string.Compare(request.NameStartsWithOrGreater, i.SortName, StringComparison.CurrentCultureIgnoreCase) < 1);
            }
            if (!string.IsNullOrEmpty(request.NameStartsWith))
            {
                items = items.Where(i => string.Compare(request.NameStartsWith, i.SortName.Substring(0, 1), StringComparison.CurrentCultureIgnoreCase) == 0);
            }

            if (!string.IsNullOrEmpty(request.NameLessThan))
            {
                items = items.Where(i => string.Compare(request.NameLessThan, i.SortName, StringComparison.CurrentCultureIgnoreCase) == 1);
            }

            var imageTypes = request.GetImageTypes().ToList();
            if (imageTypes.Count > 0)
            {
                items = items.Where(item => imageTypes.Any(item.HasImage));
            }

            var filters = request.GetFilters().ToList();

            if (filters.Contains(ItemFilter.Dislikes))
            {
                items = items.Where(i =>
                    {
                        var userdata = UserDataRepository.GetUserData(user.Id, i.GetUserDataKey());

                        return userdata != null && userdata.Likes.HasValue && !userdata.Likes.Value;
                    });
            }

            if (filters.Contains(ItemFilter.Likes))
            {
                items = items.Where(i =>
                {
                    var userdata = UserDataRepository.GetUserData(user.Id, i.GetUserDataKey());

                    return userdata != null && userdata.Likes.HasValue && userdata.Likes.Value;
                });
            }

            if (filters.Contains(ItemFilter.IsFavoriteOrLikes))
            {
                items = items.Where(i =>
                {
                    var userdata = UserDataRepository.GetUserData(user.Id, i.GetUserDataKey());

                    var likes = userdata.Likes ?? false;
                    var favorite = userdata.IsFavorite;

                    return likes || favorite;
                });
            }

            if (filters.Contains(ItemFilter.IsFavorite))
            {
                items = items.Where(i =>
                {
                    var userdata = UserDataRepository.GetUserData(user.Id, i.GetUserDataKey());

                    return userdata != null && userdata.IsFavorite;
                });
            }

            // Avoid implicitly captured closure
            var currentRequest = request;
            return items.Where(i => ApplyAdditionalFilters(currentRequest, i, user, false));
        }

        private bool ApplyAdditionalFilters(BaseItemsRequest request, BaseItem i, User user, bool isPreFiltered)
        {
            if (!isPreFiltered)
            {
                // Apply tag filter
                var tags = request.GetTags();
                if (tags.Length > 0)
                {
                    var hasTags = i as IHasTags;
                    if (hasTags == null)
                    {
                        return false;
                    }
                    if (!(tags.Any(v => hasTags.Tags.Contains(v, StringComparer.OrdinalIgnoreCase))))
                    {
                        return false;
                    }
                }

                // Apply official rating filter
                var officialRatings = request.GetOfficialRatings();
                if (officialRatings.Length > 0 && !officialRatings.Contains(i.OfficialRating ?? string.Empty))
                {
                    return false;
                }

                // Apply genre filter
                var genres = request.GetGenres();
                if (genres.Length > 0 && !(genres.Any(v => i.Genres.Contains(v, StringComparer.OrdinalIgnoreCase))))
                {
                    return false;
                }

                // Apply year filter
                var years = request.GetYears();
                if (years.Length > 0 && !(i.ProductionYear.HasValue && years.Contains(i.ProductionYear.Value)))
                {
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Filters the items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="f">The f.</param>
        /// <param name="excludeItemTypes">The exclude item types.</param>
        /// <param name="includeItemTypes">The include item types.</param>
        /// <param name="mediaTypes">The media types.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected bool FilterItem(GetItemsByName request, BaseItem f, string[] excludeItemTypes, string[] includeItemTypes, string[] mediaTypes)
        {
            // Exclude item types
            if (excludeItemTypes.Length > 0)
            {
                if (excludeItemTypes.Contains(f.GetType().Name, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Include item types
            if (includeItemTypes.Length > 0)
            {
                if (!includeItemTypes.Contains(f.GetType().Name, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Include MediaTypes
            if (mediaTypes.Length > 0)
            {
                if (!mediaTypes.Contains(f.MediaType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets all items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <returns>IEnumerable{Task{`0}}.</returns>
        protected abstract IEnumerable<TItemType> GetAllItems(GetItemsByName request, IEnumerable<BaseItem> items);
    }

    /// <summary>
    /// Class GetItemsByName
    /// </summary>
    public class GetItemsByName : BaseItemsRequest, IReturn<ItemsResult>
    {
        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        /// <value>The user id.</value>
        [ApiMember(Name = "UserId", Description = "Optional. Filter by user id, and attach user data", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid? UserId { get; set; }

        [ApiMember(Name = "NameStartsWithOrGreater", Description = "Optional filter by items whose name is sorted equally or greater than a given input string.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string NameStartsWithOrGreater { get; set; }

        [ApiMember(Name = "NameStartsWith", Description = "Optional filter by items whose name is sorted equally than a given input string.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string NameStartsWith { get; set; }

        [ApiMember(Name = "NameLessThan", Description = "Optional filter by items whose name is sorted less than a given input string.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string NameLessThan { get; set; }

        public GetItemsByName()
        {
            Recursive = true;
        }
    }
}
