# This script is an example of how you can run blender from the command line
# (in background mode with no interface) to automate tasks, in this example it
# creates a text object, camera and light, then renders and/or saves it.
# This example also shows how you can parse command line options to scripts.
#
# Example usage for this test.
#  blender --background --factory-startup --python $HOME/background_job.py -- \
#          --text="Hello World" \
#          --render="/tmp/hello" \
#          --save="/tmp/hello.blend"
#
# Notice:
# '--factory-startup' is used to avoid the user default settings from
#                     interfering with automated scene generation.
#
# '--' causes blender to ignore all following arguments so python can use them.
#
# See blender --help for details.


import bpy
import os

formats = ['obj', 'gltf']

def load_obj_and_create_material(input_file, outputFormat):

    # Clear existing objects.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    
    # Clear the current scene - useful if we want to run inside blender and retain preferences
    # for item in bpy.context.scene.objects:
    #    bpy.data.objects.remove(item, do_unlink=True)
    bpy.ops.import_scene.obj(filepath=input_file)
    
    #obj_object = bpy.context.selected_objects[0] ####<--Fix
    # make sure to get all imported objects
    obs = [ o for o in bpy.context.scene.objects if o.select_get() ]

    print('Imported objects: count = ' + str(len(obs)))
    for ob in obs:
        print(ob.name)

    numImportedPolygons = 0
    for ob in obs:
        numImportedPolygons += len(ob.data.polygons)
        
    print('Number of imported polygons = ' + str(numImportedPolygons))

    newmat = bpy.data.materials.new('newmat')
    newmat.use_nodes = True
    node_tree = newmat.node_tree

    # asign the new material to each imported mesh
    for ob in obs:
        # Assign it to object
        if ob.data.materials:
            # assign to 1st material slot
            ob.data.materials[0] = newmat
        else:
            # no slots
            ob.data.materials.append(newmat)

    nodes = node_tree.nodes
    pbdf = nodes.get("Principled BSDF")
    
    albedoFile = ''
    normalFile = ''
    ORMFile = ''

    # we want to locate and load image by name so albedo, normal and ORM
    for dirpath, dirs, files in os.walk(os.path.dirname(input_file)):
        for filename in files:
            if filename.lower().endswith('.png'):
                print(filename)
                if ('albedo' in filename.lower()):
                    albedoFile = os.path.join(dirpath, filename)
                elif ('normal' in filename.lower()):
                    normalFile = os.path.join(dirpath, filename)
                elif ('orm' in filename.lower()):
                    ORMFile = os.path.join(dirpath, filename)

    if albedoFile:
        # Create albedo node and wire it up
        img = bpy.data.images.load(albedoFile)
        albedoNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        albedoNode.image = img
        links = node_tree.links
        links.new(albedoNode.outputs['Color'], pbdf.inputs['Base Color'])

    if normalFile:
        # Create normal map node and wire it up
        img = bpy.data.images.load(normalFile)
        
        normalImageNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        normalImageNode.image = img
        normalImageNode.image.colorspace_settings.name = 'Non-Color'

        normalMapNode = newmat.node_tree.nodes.new(type='ShaderNodeNormalMap')
        links.new(normalImageNode.outputs['Color'], normalMapNode.inputs['Color'])
        links.new(normalMapNode.outputs['Normal'], pbdf.inputs['Normal'])
    
    if ORMFile:    
        # Create the ORM mapping and wire it up to the BSDF shader
        img = bpy.data.images.load(ORMFile)
        ormNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        ormNode.image = img
        
        # pipe the output from the image node into an RGB splitter node
        rgbSplitterNode = newmat.node_tree.nodes.new(type='ShaderNodeSeparateRGB')
        
        links = node_tree.links
        links.new(ormNode.outputs['Color'], rgbSplitterNode.inputs['Image'])
        links.new(rgbSplitterNode.outputs['G'], pbdf.inputs['Roughness'])        
        links.new(rgbSplitterNode.outputs['B'], pbdf.inputs['Metallic'])        
            
    # finally we need to export the obj again and hopefully it will have our material
    dirName = os.path.dirname(input_file)
    dirName = os.path.join(dirName, "converted")
    baseName = os.path.basename(input_file)
    filenameWithoutExt, _ = os.path.splitext(baseName)

    if not os.path.exists(dirName):
        os.makedirs(dirName)

    outputFormat = outputFormat.lower()

    outpathWithoutExt = os.path.join(dirName, filenameWithoutExt + '_converted')

    if outputFormat == 'gltf':
        output_file = outpathWithoutExt + '.gltf'
        print('Output File = ' + output_file)
        bpy.ops.export_scene.gltf(filepath=output_file,export_format='GLTF_SEPARATE',export_image_format='JPEG')
    elif outputFormat == 'obj':
        output_file = outpathWithoutExt + '.obj'
        print('Output File = ' + output_file)
        bpy.ops.export_scene.obj(filepath=output_file)

def supportedFormat(format):
    return format in formats 

def main():
    import sys       # to get command line args
    import argparse  # to parse options for us and print a nice help message

    # get the args passed to blender after "--", all of which are ignored by
    # blender so scripts may receive their own arguments
    argv = sys.argv

    if "--" not in argv:
        argv = []  # as if no args are passed
    else:
        argv = argv[argv.index("--") + 1:]  # get all args after "--"

    # When --help or no args are given, print this help
    usage_text = (
        "Run blender in background mode with this script:"
        "  blender --background --python " + __file__ + " -- [options]"
    )

    parser = argparse.ArgumentParser(description=usage_text)

    parser.add_argument(
        "-i", "--input", dest="input_file", metavar='FILE',
        help="The input file path",
    )

    parser.add_argument(
        "-o", "--outputFormat", dest="output_format", metavar='FILE',
        help="The output file format",
    )

    args = parser.parse_args(argv)  # In this example we won't use the args

    if not argv:
        parser.print_help()
        return

    if not args.input_file:
        print("Error: --input=\"some string\" argument not given, aborting.")
        parser.print_help()
        return

    args.output_format = args.output_format.lower()
    print(args.output_format in formats)

    if not args.output_format:
        args.output_format = 'obj'

    if not supportedFormat(args.output_format):
        print("Error: --outputFormat=\"some string\" argument invalid, aborting.")
        parser.print_help()
        return

    # Run the example function
    load_obj_and_create_material(args.input_file, args.output_format)

    print("batch job finished, exiting")

if __name__ == "__main__":
    main()
