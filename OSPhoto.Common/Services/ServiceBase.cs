using Microsoft.EntityFrameworkCore;
using OSPhoto.Common.Database;
using OSPhoto.Common.Interfaces;
using OSPhoto.Common.Models;
using OSPhoto.Common.Services.Models;
using DbPhoto = OSPhoto.Common.Database.Models.Photo;

namespace OSPhoto.Common.Services;

public class ServiceBase(ApplicationDbContext dbContext, IFileSystem fileSystem, ILogger logger) : IServiceBase
{
    protected string _mediaPath = Environment.GetEnvironmentVariable("MEDIA_PATH");

    public ItemBase GetInfo(string id)
    {
        var photoPath = Path.Combine(_mediaPath, ItemBase.GetPathFromId(id));
        return ItemBase.ConvertToItemBase(fileSystem.FileInfo.New(photoPath), _mediaPath, dbContext);
    }

    public async Task EditInfo(string id, string title, string description, int? importedShareId = null)
    {
        try
        {
            var photo = await dbContext.Photos.FindAsync(id);
            if (photo == null)
            {
                await dbContext.Photos.AddAsync(new DbPhoto(
                    id,
                    Path.Combine(_mediaPath, ItemBase.GetPathFromId(id)),
                    title,
                    description,
                    importedShareId));
            }
            else
            {
                photo.Title = title;
                photo.Description = description;
                photo.ImportedShareId = importedShareId;
                photo.UpdatedDate = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception editing metadata for {id}; given {title}, {description}", id, title, description);
        }
    }

    public async Task<MoveResult> Move(string id, string destinationAlbumId, bool isOverwrite)
    {
        var itemPath = Path.Combine(_mediaPath, ItemBase.GetPathFromId(id));

        var destinationPath = Path
            .Join(
                _mediaPath,
                ItemBase.GetPathFromId(destinationAlbumId),
                Path.GetFileName(itemPath));

        var newId = ItemBase.GetIdForPath(_mediaPath, fileSystem.FileInfo.New(destinationPath), $"{id.Split('_').First()}_");

        try
        {
            if (!System.IO.File.Exists(destinationPath) || (System.IO.File.Exists(destinationPath) && isOverwrite))
            {
                System.IO.File.Move(itemPath, destinationPath);

                // is there any metadata that we need to move as well?
                var photoRecord = await dbContext.Photos.FindAsync(id);
                if (photoRecord != null)
                {
                    await dbContext.Photos.AddAsync(new DbPhoto(
                        newId,
                        destinationPath,
                        photoRecord.Title,
                        photoRecord.Description,
                        photoRecord.ImportedShareId));

                    dbContext.Photos.Remove(photoRecord);

                    await dbContext.SaveChangesAsync();
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error moving photo (id: {id}) from {src} to {dest}",
                id,
                itemPath,
                destinationPath);

            return new MoveResult(false);
        }

        return new MoveResult(true, newId);
    }

    public async Task Delete(string id)
    {
        var itemPath = Path.Combine(_mediaPath, ItemBase.GetPathFromId(id));

        try
        {
            if (System.IO.File.Exists(itemPath))
                System.IO.File.Delete(itemPath);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error deleting: {path}", itemPath);
        }

        try
        {
            await dbContext
                .Photos
                .Where(p => p.Id == id)
                .ExecuteDeleteAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error deleting metadata for id: {id}", id);
        }
    }
}
